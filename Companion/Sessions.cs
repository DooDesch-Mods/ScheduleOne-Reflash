using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Reflash.Companion
{
    /// <summary>
    /// Who is allowed to talk to the companion server, and how they got that way.
    ///
    /// The shape is: a short-lived pairing token goes on screen as a QR code, a phone posts it once, and gets a
    /// session id back as a cookie. The token is then burnt.
    ///
    /// The token lives in the URL FRAGMENT, which never leaves the browser - it is in no request, no proxy log and
    /// no Referer header. That is why the QR points at a plain page and the shell posts the fragment itself rather
    /// than the token being a path or a query parameter.
    ///
    /// This is a plaintext channel on a local network, and it is honest about that: the token is short-lived, the
    /// server is off by default, and nothing here pretends a LAN is private. What it does guarantee is that
    /// reaching the port is not the same as being able to use it.
    ///
    /// EVERYTHING here is touched from at least two threads and the collections are chosen accordingly. Connection
    /// threads pair, look up and read; the Unity main thread broadcasts through the host taps. A plain Dictionary
    /// here would corrupt or throw the first time a phone was connected while the game raised an event - which is
    /// to say, immediately.
    /// </summary>
    internal sealed class Sessions
    {
        /// <summary>Two minutes. Long enough to fetch a phone, short enough that a QR left on a stream or in a
        /// screenshot stops being a key.</summary>
        private const double PairingWindowSeconds = 120;

        private const int MaxSessions = 4;

        /// <summary>
        /// How long a session survives without being seen. A phone that was put in a pocket keeps its stream open
        /// and stays fresh; one that walked out of the house stops counting against the limit.
        ///
        /// Without this, four abandoned sessions block pairing forever - including sessions created when the
        /// pairing response never reached the phone, which is exactly the case someone would retry.
        /// </summary>
        private const double SessionIdleSeconds = 300;

        /// <summary>A wrong guess is forgotten after this long. Five failures should slow an attacker down, not
        /// lock a household out of its own phone until the game restarts.</summary>
        private const double AttemptResetSeconds = 300;

        private readonly ConcurrentDictionary<string, Session> _sessions =
            new ConcurrentDictionary<string, Session>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, Attempts> _pairAttempts =
            new ConcurrentDictionary<string, Attempts>(StringComparer.Ordinal);

        private sealed class Attempts
        {
            internal int Count;
            internal double FirstAt;
        }

        /// <summary>Guards the token, which is a read-modify-write across two fields and cannot be made atomic by
        /// choosing a different collection.</summary>
        private readonly object _tokenLock = new object();

        private string _token = "";
        private double _tokenBornAt = -1;

        internal sealed class Session
        {
            internal string Id;
            internal string Device = "";
            internal double LastSeen;

            /// <summary>Filled by the main thread through a host tap, drained by this session's own SSE thread.</summary>
            internal readonly ConcurrentQueue<string> Outbox = new ConcurrentQueue<string>();
        }

        /// <summary>The pairing token, minting a new one if none is live. What goes into the QR.</summary>
        internal string CurrentToken(double now)
        {
            // The connect screen reads this regularly, which makes it the natural place to tidy up.
            Sweep(now);

            lock (_tokenLock)
            {
                if (_tokenBornAt >= 0 && now - _tokenBornAt < PairingWindowSeconds && _token.Length > 0) return _token;

                _token = RandomId(12);   // 96 bits
                _tokenBornAt = now;
                return _token;
            }
        }

        /// <summary>
        /// Throw the current code away and mint a fresh one. How a second device is paired, since a token admits
        /// exactly one.
        ///
        /// An explicit method rather than asking for a token with a doctored timestamp - that trick would also hand
        /// the doctored time to <see cref="Sweep"/> and quietly disconnect every device that was already paired.
        /// </summary>
        internal string NewToken(double now)
        {
            lock (_tokenLock)
            {
                _token = RandomId(12);
                _tokenBornAt = now;
                return _token;
            }
        }

        /// <summary>Whether a token is still worth showing, so the connect screen can say "expired" rather than
        /// silently handing out one that will be refused.</summary>
        internal double TokenAgeSeconds(double now)
        {
            lock (_tokenLock) return _tokenBornAt < 0 ? double.MaxValue : now - _tokenBornAt;
        }

        internal int Count => _sessions.Count;

        /// <summary>
        /// Trade a pairing token for a session. Rate-limited per address so the token cannot be guessed by
        /// hammering, and the token is burnt on success - a second phone needs the QR shown again, which is a
        /// deliberate friction rather than an oversight.
        /// </summary>
        /// <summary>Why a pairing was refused, so the phone can say something true instead of always blaming the code.</summary>
        internal enum Refusal { None, LockedOut, Expired, WrongCode, TooManyDevices }

        internal Session Pair(string token, string device, string remote, double now) =>
            Pair(token, device, remote, now, out _);

        internal Session Pair(string token, string device, string remote, double now, out Refusal why)
        {
            why = Refusal.None;
            Sweep(now);

            if (_pairAttempts.TryGetValue(remote, out Attempts attempts)
                && attempts.Count >= 5
                && now - attempts.FirstAt < AttemptResetSeconds)
            {
                why = Refusal.LockedOut;
                return null;
            }

            // The whole check-and-burn is inside the lock. Split, two devices could both pass the comparison
            // before either invalidated the token, and a single-use code would admit them both.
            lock (_tokenLock)
            {
                if (_tokenBornAt < 0 || now - _tokenBornAt > PairingWindowSeconds) { why = Refusal.Expired; return null; }

                if (!FixedTimeEquals(token, _token))
                {
                    _pairAttempts.AddOrUpdate(
                        remote,
                        _ => new Attempts { Count = 1, FirstAt = now },
                        (_, a) =>
                        {
                            if (now - a.FirstAt >= AttemptResetSeconds) { a.Count = 1; a.FirstAt = now; }
                            else a.Count++;
                            return a;
                        });

                    why = Refusal.WrongCode;
                    return null;
                }

                if (_sessions.Count >= MaxSessions) { why = Refusal.TooManyDevices; return null; }

                _token = "";
                _tokenBornAt = -1;
            }

            _pairAttempts.TryRemove(remote, out _);

            var session = new Session { Id = RandomId(16), Device = device ?? "", LastSeen = now };
            _sessions[session.Id] = session;
            return session;
        }

        internal Session Find(string sessionId, double now)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            if (!_sessions.TryGetValue(sessionId, out Session session)) return null;

            session.LastSeen = now;
            return session;
        }

        /// <summary>
        /// Forget what has gone quiet. Called on the paths that care about the limits - pairing and the periodic
        /// token read - rather than on a timer, because there is nothing here worth a thread of its own.
        /// </summary>
        internal void Sweep(double now)
        {
            foreach (KeyValuePair<string, Session> pair in _sessions)
                if (now - pair.Value.LastSeen > SessionIdleSeconds) _sessions.TryRemove(pair.Key, out _);

            foreach (KeyValuePair<string, Attempts> pair in _pairAttempts)
                if (now - pair.Value.FirstAt > AttemptResetSeconds) _pairAttempts.TryRemove(pair.Key, out _);
        }

        internal void Drop(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId)) _sessions.TryRemove(sessionId, out _);
        }

        internal void DropAll() => _sessions.Clear();

        /// <summary>
        /// Queue an envelope for every connected device. Called from the Unity main thread inside whatever raised
        /// the event, so it must return immediately - it only enqueues.
        /// </summary>
        internal void Broadcast(string envelope)
        {
            foreach (KeyValuePair<string, Session> pair in _sessions)
            {
                Session s = pair.Value;

                // A device that stopped reading must not grow an unbounded queue - it is either coming back in a
                // moment or gone, and either way the newest state is the only one worth having.
                if (s.Outbox.Count > 256)
                    while (s.Outbox.TryDequeue(out _)) { }

                s.Outbox.Enqueue(envelope);
            }
        }

        private static string RandomId(int bytes)
        {
            var buf = new byte[bytes];
            RandomNumberGenerator.Fill(buf);

            // base64url: safe in a URL fragment and in a cookie without escaping.
            return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>Constant-time compare, so a wrong token cannot be narrowed down by how long the refusal took.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

            byte[] x = Encoding.UTF8.GetBytes(a);
            byte[] y = Encoding.UTF8.GetBytes(b);
            return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
        }
    }
}
