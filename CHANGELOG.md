# Changelog

All notable changes to Reflash are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-10-29

### Added

- No code to scan: tap **Allow** in Connect once, and the phone gets back in by itself, also after a game restart.
  **Forget** removes it.
- Your phone no longer needs the PC's Wi-Fi: on mobile data it connects through relay.doodesch.de, end to end
  encrypted. **Same Wi-Fi only** in Connect turns that off.
- Android app: Reflash - S1 mod companion finds your game on the Wi-Fi and opens your mod apps full screen. Closed
  test on Google Play, [join here](https://groups.google.com/g/reflash-testers).

### Fixed

- Buttons in mod apps on your real phone (Yesla's honk, lock, summon) act once per tap. They could fire again on
  their own or not at all.

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
