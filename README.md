# Reflash - the Schedule I phone, on your phone

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/reflash](https://support.doodesch.de/reflash).

> The seven apps on the in game phone, rebuilt as web pages and served to the phone in your pocket. Open
> Connect, scan the code, and messages, the map, deliveries, products, dealers, contacts and the journal are
> on a second screen - same save, same moment, while the game keeps the whole monitor.
>
> Built on [Sideload](https://github.com/DooDesch-Mods/ScheduleOne-Sideload), which is a hard requirement.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Sideload](https://img.shields.io/badge/Sideload-1.1.0+-orange)
![Multiplayer](https://img.shields.io/badge/multiplayer-co--op-blue)

**[Sideload](https://github.com/DooDesch-Mods/ScheduleOne-Sideload)** · **[Support](https://support.doodesch.de/reflash)**

## What you get

A **Connect** app on the in game phone. Turn the companion on in it, scan the QR code with your phone
camera, and your phone opens the same phone: a home screen with the same icons in the same order, and behind
them the seven apps.

- **Messages** - every conversation, the thread, the replies, and the deal window with its clock.
- **Map** - the real map picture with the pins on it, dragged with a finger, pinched to zoom.
- **Deliveries** - the shops, their stock and prices, and an order you can place from the sofa.
- **Products** - your products, their effects in the game's own colours, and what they list for.
- **Dealers** - who you recruited, their region, their cut and how full their hands are.
- **Contacts** - the relationship graph, and a customer's standards, dependence and orders.
- **Journal** - the quests the game would show you, with their steps.

Every number is read from the same manager the vanilla screen reads, and every action goes through the same
server call the vanilla screen makes, so a delivery ordered from the sofa is a delivery, and co-op sees it
the way it always did.

## The in game phone is untouched

Out of the box Reflash adds one icon and changes nothing else. The seven icons on the in game phone still
open the game's own screens.

That is deliberate, and it is the plan rather than a limitation. These seven screens are new, and a phone
you are holding is a much better place to find out what is wrong with them than the phone you rely on to
play. So they ship on the second screen first, and they move onto the in game phone one at a time as each
one earns it.

If you want them there now, the Connect app has a switch for it - **Use these apps here too**. No restart,
and the same switch puts the originals back. It is also `ReplaceVanillaApps` in `MelonPreferences.cfg`.

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | IL2CPP (current Steam public build) |
| MelonLoader | `0.7.3+` |
| Sideload | `1.1.0+` ([Thunderstore](https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/)) |
| A phone or tablet | any browser, on the same network as the PC |

## Installation

### Recommended: a Thunderstore mod manager

Install **Reflash** with r2modman or the Thunderstore app. Sideload comes with it as a dependency.

### Manual

1. Install [MelonLoader](https://melonwiki.xyz/) `0.7.3+` and run the game once.
2. Drop `Sideload.dll` and `Reflash.dll` into `Schedule I/Mods/`.

## Pairing a phone

1. Open **Connect** on the in game phone and press **Turn on**. The server starts and a QR code appears.
2. Scan it with your phone's camera. The link is plain `http://` on your own network, so any browser opens it.
3. The phone asks for nothing else. It is paired, and the home screen is there.

If nothing happens after scanning, it is almost always one of two things. Either the phone is not on the
same network as the PC - guest Wi-Fi and mobile data both count as somewhere else - or Windows Firewall is
holding the port. A dialog you clicked away once leaves a block rule behind that no later prompt overrides;
allow the game on private networks and try again.

The address is printed under the code as text as well, because a QR code on a stream or across a room is
often unreadable and typing it has to stay possible.

Up to four devices at a time. Each one needs its own code: press **New code** and scan again.

## What it opens on your network

While the companion is on, the game listens on TCP `6180` on every interface. That is the point - a phone
has to be able to reach it - so here is exactly what is there:

- **A device that has not paired can read one thing**, `/health`, which answers "something is here" and
  nothing else. Not the number of connected devices, not your save, not a name.
- **Pairing needs the code on screen.** 96 random bits, valid for two minutes or until it is used, and burnt
  either way. Wrong guesses lock a device out for a minute.
- **A paired device gets a cookie**, `HttpOnly` and `SameSite=Strict`, so no other page in that browser can
  make a request that carries it. There are no CORS headers anywhere: nothing cross-origin has business here.
- **The Host header is checked** against the addresses this server actually answers on, so a name someone
  else controls cannot be pointed at your port to read the replies.
- **The companion is off until you press the button**, and the button remembers. Installing the mod opens
  no port.

It is plain HTTP, because it has to be: a browser will not let an `https` page reach a LAN address, and a
certificate for `192.168.x.x` is not a thing that exists. Treat it as what it is - a screen on your own
network, not something to forward through a router.

## Configuration

`UserData/MelonPreferences.cfg`, section `Reflash_01_Main`. You should not need to open it; the Connect app
sets the first two.

| Setting | Default | What it does |
|---|---|---|
| `ReplaceVanillaApps` | `false` | OFF: the icons on the in game phone open the game's own screens and Reflash only adds Connect. ON: they open the Reflash ones. The Connect app switches this without a restart. |
| `Companion` | `false` | Whether the server runs. Turning it on in the Connect app sets this, so it comes back next session. Off on a fresh install because it opens a port. |
| `CompanionPort` | `6180` | The TCP port. Change it only if something else has that one; the port is part of the address in the QR code. |

## On the phone, not just in a browser

The page knows when it is being read on a real device rather than by the in game renderer, and uses it:

- **Full screen** on the first tap, with the Android back gesture wired to the phone's own back - one press,
  one step back, out of a thread before out of an app.
- **The screen stays awake** while an app is open, so the map does not black out while you are reading it.
- **A short buzz** on a press that did something, where the device has a vibrator.
- **The map and the graph pan with a finger** and carry momentum, and pinch zooms them.
- **It follows the device.** The short side of the screen is fixed at 400 CSS pixels, the long side is
  whatever your phone actually has, and turning the device turns the app.

## Multiplayer

Everything a Reflash app writes goes through the same `[ServerRpc(RequireOwnership = false)]` the vanilla
screen calls, so a client acts and the host rebroadcasts, exactly as before. A write that arrives against a
stale view is refused with `err:stale` and the page re-reads rather than sending an order for a price that
changed a second ago.

Each player pairs their own phone to their own game. Nothing about the companion crosses to other players.

## Layout of the mod

```
Reflash/
  Core.cs               MelonMod: register the eight apps, patch, start the companion
  Prefs.cs              three settings, two of them switched from inside the game
  Pulse.cs              the revision loop - every emit to a page comes from here, never from a handler
  Wire/                 the wire protocol and the view types. NO engine reference, so the headless suite
                        compiles them in a second and catches an accidental dependency there
  Screens/              one file per app: what its page can ask for and what it may do
  Game/                 the IL2CPP adapters. Everything that touches a manager lives here and nowhere else
  Hijack/               seven Harmony prefixes on App.SetOpen(bool) - the one point every way of opening a
                        vanilla app passes through, icon, number key, hardware shortcut and cross-open alike
  Companion/            the server: TcpListener, SSE downstream, POST upstream, pairing, the QR encoder
  Assets/reflash-*/     the eight bundles - index.html, app.css, app.js
  Assets/shell/         the companion's own page and the bridge that makes one bundle run in both worlds
```

A bundle is **byte identical** in both worlds. The server injects three lines into the `<head>` on the way
out - a `<base>`, a compatibility stylesheet and the bridge - and the file on disk is never touched. That is
what keeps the second screen from drifting into a different app that happens to look similar.

`Sideload.dll` is never referenced. Contact runs through the single file `Sideload.Api` shim, which finds
the host by reflection, so without Sideload installed Reflash registers nothing, patches nothing and says so
in the log.

## Compatibility

- IL2CPP build only (current Steam public branch).
- Runs alongside other mods. With `ReplaceVanillaApps` off it adds one home screen icon and patches nothing
  that fires.
- The seven prefixes are applied and guarded one by one. If a future game update moves one of them, that one
  vanilla app keeps working and the log says which.
- The bundles can be overridden per app: a folder `Mods/reflash-map/` with an `app.css` in it wins over the
  one compiled in, on both screens at once. That is Sideload's mechanism, not a special case here.

## Credits

- **DooDesch** - mod author.
- **[Sideload](https://github.com/DooDesch-Mods/ScheduleOne-Sideload)** - the renderer this is written against.
- **[QrLite](https://github.com/DooDesch/QrLite)** (MIT) - the QR encoder, vendored as one file.
- **TVGS** - Schedule I.

## License

MIT. See [LICENSE.md](LICENSE.md).
