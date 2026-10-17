# Changelog

All notable changes to Reflash are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-10-17

### Added

- Connect app on the in game phone: turn it on, scan the code with your real phone, and the game's phone opens
  there. Up to four devices.
- All seven apps on the second screen: messages, map, deliveries, products, dealers, contacts and the journal,
  each laid out like the game's own.
- Place delivery orders and send replies from your real phone. They go through the game's own calls, so co-op
  sees them as usual.
- Made for a phone in your hand: full screen on the first tap, the back gesture works, the screen stays awake,
  and the map and contacts pan and pinch.
- `ReplaceVanillaApps` puts the rebuilt apps on the in game phone too. Off by default, and Connect switches it
  without a restart.

### Security

- The companion is off until you turn it on in Connect, so installing the mod opens no port.
- Pairing needs the code on screen. A code works once, expires after two minutes, and wrong guesses lock it out.
- Plain HTTP on your own network. Do not forward port `6180` through your router.
