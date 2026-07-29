# Changelog

All notable changes to Reflash are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-07-29

First release. Reflash rebuilds the seven apps on the in game phone as web pages and serves them to a real
phone on your network, so the phone in your pocket shows the phone in the game while the game keeps the
whole monitor.

### Added

- **Connect**, on the in game phone. Turn the companion on there, scan the QR code with a phone camera, and
  that phone opens the same home screen with the same icons in the same order. The address is printed as
  text under the code as well, because a QR code on a stream or across a room is often unreadable. Up to
  four devices, one code each.
- **The seven apps**, on the second screen. Messages with the thread, the replies and the deal window's
  clock; the map with the real map picture and its pins; deliveries with stock, prices and an order you can
  place; products with their effects in the game's own colours; dealers with region, cut and how full their
  hands are; contacts with the relationship graph and a customer's standards, dependence and orders; and the
  journal with the quests the game would show you.
- **Nothing is reimplemented.** Every number is read from the manager the vanilla screen reads, and every
  action goes through the same `[ServerRpc(RequireOwnership = false)]` the vanilla screen calls, so co-op
  behaves as it always did and a balancing patch reaches these screens on its own.
- **A switch for the in game phone.** Off by default: the seven icons still open the game's own screens and
  Reflash adds one icon and nothing else. **Use these apps here too** in the Connect app puts the
  replacements on the icons instead, without a restart, and puts them back the same way. Also
  `ReplaceVanillaApps` in `MelonPreferences.cfg`.
- **Built for a phone you hold.** Full screen on the first tap with the Android back gesture
  wired to the phone's own back, the screen kept awake while an app is open, a short buzz on a press that
  did something, finger panning with momentum on the map and the graph, pinch to zoom, and a layout that
  follows the real screen instead of assuming one.
- **One bundle, two worlds.** The same files run in the game and in the browser: the server injects a
  `<base>`, a compatibility stylesheet and the bridge into the `<head>` on the way out and never touches the
  file on disk. An override folder under `Mods/reflash-<app>/` replaces a stylesheet on both screens at once.

### Security

- The companion is off until you press the button, so installing the mod opens no port.
- An unpaired device can read `/health` and nothing else - not the number of connected devices, not a name.
- Pairing needs the code on screen: 96 random bits, two minutes or one use, burnt either way, with a lockout
  after repeated wrong guesses.
- A paired device holds an `HttpOnly` / `SameSite=Strict` cookie, there are no CORS headers anywhere, and
  the `Host` header is checked against the addresses the server really answers on.
- Plain HTTP, unavoidably: a browser will not let an `https` page reach a LAN address and there is no
  certificate for `192.168.x.x`. It is a screen on your own network, not something to forward through a router.
