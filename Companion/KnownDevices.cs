using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MelonLoader.Utils;

namespace Reflash.Companion
{
    /// <summary>
    /// Phones the player allowed once, and the requests still waiting for that answer.
    ///
    /// The companion app holds one random key per install. It never travels anywhere but the pairing request, and
    /// only its SHA-256 is kept here, so the file on disk cannot be replayed as a key. A remembered device gets a
    /// session without the code on screen, also after a game restart; that is the whole point of remembering it.
    ///
    /// An unknown device becomes a request the player answers in Connect. The phone keeps asking with the same key
    /// until the answer is there. Requests are capped per address and in total, so a device on the network cannot
    /// bury the player in prompts, and three refusals in five minutes stop an address from asking at all.
    ///
    /// Touched from connection threads (pairing) and from the main thread (Allow, Deny, Forget, the Connect page),
    /// so one lock guards both lists and the file.
    /// </summary>
    internal sealed class KnownDevices
    {
        private const double RequestSeconds = 120;
        private const int MaxPending = 3;
        private const int MaxNameChars = 40;
        private const int MaxDenials = 3;
        private const double DenialWindowSeconds = 300;

        private readonly object _lock = new object();
        private readonly List<Device> _devices = new List<Device>();
        private readonly Dictionary<string, Request> _pending = new Dictionary<string, Request>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<double>> _denials = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        private readonly string _path;
        private int _revision;

        internal sealed class Device
        {
            /// <summary>Lowercase hex SHA-256 of the device key.</summary>
            internal string Hash = "";
            internal string Name = "";
            internal DateTime FirstPaired;
            internal DateTime LastSeen;

            /// <summary>The session this device holds now, so pairing again replaces it. Not saved: sessions die
            /// with the game.</summary>
            internal string SessionId = "";

            /// <summary>What Connect addresses it by. A prefix of the hash, which already says nothing about the key.</summary>
            internal string Id => Hash.Substring(0, 12);
        }

        internal enum RequestState { Waiting, Allowed, Denied }

        internal sealed class Request
        {
            internal string Id = "";
            internal string Remote = "";
            internal string Hash = "";
            internal string Name = "";
            internal double BornAt;
            internal RequestState State;
        }

        /// <summary>What a pairing attempt comes to, before any session exists.</summary>
        internal enum Verdict { Known, Waiting, NewRequest, Denied, Expired, Forgotten, Busy, Throttled }

        internal KnownDevices()
        {
            _path = Path.Combine(MelonEnvironment.UserDataDirectory, "Reflash", "devices.json");
            Load();
        }

        /// <summary>Changes whenever a list Connect shows changes, so the page re-reads.</summary>
        internal int Revision { get { lock (_lock) return _revision; } }

        internal static bool KeyLooksRight(string key)
        {
            if (key == null || key.Length != 64) return false;
            foreach (char c in key)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        internal static string CleanName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
                if (!char.IsControl(c)) sb.Append(c);

            string clean = sb.ToString().Trim();
            return clean.Length > MaxNameChars ? clean.Substring(0, MaxNameChars).TrimEnd() : clean;
        }

        internal static string HashOf(string key)
        {
            using SHA256 sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(Encoding.ASCII.GetBytes(key));
            var sb = new StringBuilder(64);
            foreach (byte b in digest) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// One ask from a phone. Decides only; the caller makes the session for <see cref="Verdict.Known"/> and then
        /// reports it with <see cref="Admitted"/>. <paramref name="request"/> is set for the verdicts that concern
        /// one, so the caller can log and notify.
        /// </summary>
        internal Verdict Ask(string hash, string name, string remote, double now, out Device device, out Request request)
        {
            device = null;
            request = null;

            lock (_lock)
            {
                SweepExpired(now, remote);

                device = Find(hash);
                _pending.TryGetValue(remote, out Request mine);

                if (device != null)
                {
                    // Allowed, or known from before. Either way its request has done its job.
                    if (mine != null && mine.Hash == hash) { _pending.Remove(remote); _revision++; }
                    return Verdict.Known;
                }

                if (mine != null && mine.Hash == hash)
                {
                    request = mine;
                    switch (mine.State)
                    {
                        case RequestState.Denied:
                            _pending.Remove(remote);
                            _revision++;
                            return Verdict.Denied;

                        case RequestState.Allowed:
                            // Allowed, then forgotten before the phone asked again.
                            _pending.Remove(remote);
                            _revision++;
                            return Verdict.Forgotten;
                    }

                    if (now - mine.BornAt > RequestSeconds)
                    {
                        _pending.Remove(remote);
                        _revision++;
                        return Verdict.Expired;
                    }

                    return Verdict.Waiting;
                }

                // A different device from the same address, while one is still waiting: one at a time.
                if (mine != null) { request = mine; return Verdict.Busy; }

                if (TooManyDenials(remote, now)) return Verdict.Throttled;
                if (_pending.Count >= MaxPending) return Verdict.Busy;

                request = new Request
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 12),
                    Remote = remote,
                    Hash = hash,
                    Name = name,
                    BornAt = now,
                    State = RequestState.Waiting,
                };
                _pending[remote] = request;
                _revision++;
                return Verdict.NewRequest;
            }
        }

        /// <summary>A known device got a session. Remembers which, so the next pairing replaces it, and when it was
        /// last seen.</summary>
        internal void Admitted(Device device, string sessionId, string name)
        {
            lock (_lock)
            {
                device.SessionId = sessionId ?? "";
                device.LastSeen = DateTime.UtcNow;
                if (name.Length > 0) device.Name = name;
                _revision++;
                Save();
            }
        }

        /// <summary>The player pressed Allow. Null when the request is gone: expired, or answered on another screen.</summary>
        internal Request Allow(string requestId, double now)
        {
            lock (_lock)
            {
                Request r = Waiting(requestId, now);
                if (r == null) return null;

                r.State = RequestState.Allowed;
                if (Find(r.Hash) == null)
                {
                    DateTime at = DateTime.UtcNow;
                    _devices.Add(new Device { Hash = r.Hash, Name = r.Name, FirstPaired = at, LastSeen = at });
                }

                _revision++;
                Save();
                return r;
            }
        }

        internal Request Deny(string requestId, double now)
        {
            lock (_lock)
            {
                Request r = Waiting(requestId, now);
                if (r == null) return null;

                r.State = RequestState.Denied;
                if (!_denials.TryGetValue(r.Remote, out List<double> times)) _denials[r.Remote] = times = new List<double>();
                times.Add(now);

                _revision++;
                return r;
            }
        }

        /// <summary>Forget one device. Returns it, so the caller can drop its session; null when there is no such device.</summary>
        internal Device Forget(string deviceId)
        {
            lock (_lock)
            {
                int at = _devices.FindIndex(d => d.Id == deviceId);
                if (at < 0) return null;

                Device gone = _devices[at];
                _devices.RemoveAt(at);
                _revision++;
                Save();
                return gone;
            }
        }

        /// <summary>Forget every device. Returns how many there were.</summary>
        internal int ForgetAll()
        {
            lock (_lock)
            {
                int count = _devices.Count;
                if (count == 0) return 0;

                _devices.Clear();
                _revision++;
                Save();
                return count;
            }
        }

        internal List<Device> Devices()
        {
            lock (_lock) return new List<Device>(_devices);
        }

        /// <summary>Requests still waiting for the player, oldest first.</summary>
        internal List<Request> Waiting(double now)
        {
            lock (_lock)
            {
                SweepExpired(now, null);
                var list = new List<Request>();
                foreach (Request r in _pending.Values)
                    if (r.State == RequestState.Waiting && now - r.BornAt <= RequestSeconds) list.Add(r);
                list.Sort((a, b) => a.BornAt.CompareTo(b.BornAt));
                return list;
            }
        }

        private Request Waiting(string requestId, double now)
        {
            foreach (Request r in _pending.Values)
                if (r.Id == requestId && r.State == RequestState.Waiting && now - r.BornAt <= RequestSeconds) return r;
            return null;
        }

        private Device Find(string hash)
        {
            foreach (Device d in _devices)
                if (d.Hash == hash) return d;
            return null;
        }

        /// <summary>
        /// Requests nobody answered and the phone stopped asking about. The one from <paramref name="keep"/> stays,
        /// because that address is asking right now and is owed "expired" rather than a fresh prompt.
        /// </summary>
        private void SweepExpired(double now, string keep)
        {
            List<string> stale = null;
            foreach (KeyValuePair<string, Request> pair in _pending)
            {
                if (pair.Key == keep) continue;
                // Twice the window: an answered or expired request waits one more window for its phone to ask.
                if (now - pair.Value.BornAt > RequestSeconds * 2) (stale ??= new List<string>()).Add(pair.Key);
            }

            if (stale == null) return;
            foreach (string remote in stale) _pending.Remove(remote);
            _revision++;
        }

        private bool TooManyDenials(string remote, double now)
        {
            if (!_denials.TryGetValue(remote, out List<double> times)) return false;
            times.RemoveAll(t => now - t > DenialWindowSeconds);
            if (times.Count == 0) { _denials.Remove(remote); return false; }
            return times.Count >= MaxDenials;
        }

        // ----- the file -----

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    Core.Log.Msg("no remembered devices yet.");
                    return;
                }

                foreach (string item in JsonRead.Objects(File.ReadAllText(_path)))
                {
                    string hash = JsonRead.Field(item, "hash");
                    if (!KeyLooksRight(hash))
                    {
                        Core.Log.Warning("a remembered device without a valid hash was skipped.");
                        continue;
                    }

                    _devices.Add(new Device
                    {
                        Hash = hash,
                        Name = CleanName(JsonRead.Field(item, "name")),
                        FirstPaired = ParseTime(JsonRead.Field(item, "first")),
                        LastSeen = ParseTime(JsonRead.Field(item, "seen")),
                    });
                }

                Core.Log.Msg($"{_devices.Count} remembered device(s) loaded.");
            }
            catch (Exception e)
            {
                Core.Log.Error($"could not read remembered devices from {_path}: {e.Message}");
            }
        }

        /// <summary>Written through a temporary file, so a game killed mid-write leaves the old list rather than half
        /// of a new one.</summary>
        private void Save()
        {
            try
            {
                var sb = new StringBuilder("[\n");
                for (int i = 0; i < _devices.Count; i++)
                {
                    Device d = _devices[i];
                    sb.Append("  {\"hash\":").Append(JsonRead.Quote(d.Hash))
                      .Append(",\"name\":").Append(JsonRead.Quote(d.Name))
                      .Append(",\"first\":").Append(JsonRead.Quote(FormatTime(d.FirstPaired)))
                      .Append(",\"seen\":").Append(JsonRead.Quote(FormatTime(d.LastSeen)))
                      .Append(i < _devices.Count - 1 ? "},\n" : "}\n");
                }
                sb.Append("]\n");

                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".tmp";
                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception e)
            {
                Core.Log.Error($"could not save remembered devices to {_path}: {e.Message}");
            }
        }

        private static string FormatTime(DateTime t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        private static DateTime ParseTime(string s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)
                ? t
                : DateTime.UtcNow;
    }
}
