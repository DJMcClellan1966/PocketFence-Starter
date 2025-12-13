Mobile Hotspot Native Integration (PocketFence)

Overview

The Dart code registers a MethodChannel `pocketfence.hotspot` and calls methods:
- `startHotspot` (args: `{ name: String, blockOthers: bool }`)
- `stopHotspot` (no args)
- `setHotspotName` (args: `{ name: String }`)

This document describes native implementation options, limitations, and sample scaffolds for Android and iOS.

Android

- APIs:
  - Android's tethering (Wi-Fi hotspot) control APIs are largely hidden/private and require system or carrier-signed apps to modify programmatically in many versions. Public alternatives:
    - `startLocalOnlyHotspot()` from `WifiManager` (Android 8+): starts a local-only hotspot (clients can connect but there is no internet sharing). Apps can request it but it offers limited control and cannot set persistent SSID reliably across reboots.
    - For full tethering control (set SSID, password, enable/disable), you typically need privileged/system APIs or use reflection/hidden APIs (not suitable for Play Store distribution).
  - Permissions:
    - `ACCESS_FINE_LOCATION` and `CHANGE_WIFI_STATE` may be required.
    - `Manifest.permission.MANAGE_WIFI` is signature|system-level on many devices.

- Recommended approach (safe, limited):
  - Use `WifiManager.startLocalOnlyHotspot()` and return the generated SSID and password to the Dart side.
  - Document to users that blocking other Wi-Fi requires either a VPN-based routing approach (create local VPN to filter routes) or advanced device admin capabilities.

- Implementation note:
  - `MainActivity.kt` now includes a `startLocalOnlyHotspot()` implementation that uses `WifiManager.startLocalOnlyHotspot()` and returns the generated SSID and password to Dart via the `pocketfence.hotspot` MethodChannel.
  - The native code requires `ACCESS_FINE_LOCATION` runtime permission. The app must request that permission from the user before invoking `startHotspot` from Dart; otherwise the native method will return a `MISSING_PERMISSION` error.
  - The method returns a map `{ "ssid": <ssid>, "password": <presharedKey> }` on success. Call `stopHotspot` to close the reservation.

  - Sample Kotlin behavior:
    - On `startHotspot` (Dart -> native) the native code will attempt to start a LocalOnlyHotspot and, if successful, return an object containing credentials.
    - On `stopHotspot` the native code will close the hotspot reservation.

iOS

- Limitations:
  - iOS does not provide public APIs for third-party apps to programmatically enable/disable Personal Hotspot or change the hotspot name. The hotspot name is derived from the device name (`Settings > General > About > Name`).
  - Options for policy enforcement (blocking other Wi-Fi access) typically involve:
    - VPN-based filtering (NETunnelProvider) to intercept and filter traffic.
    - Mobile Device Management (MDM) profiles for supervised devices.

- Recommended approach:
  - Provide UI that deep-links to Settings to help users enable Personal Hotspot and guide them to rename the device if necessary.
  - Use a NETunnelProvider (VPN) extension to enforce DNS filtering or block traffic when needed.

General: Blocking "other Wi-Fi" for clients

- For clients connecting to a hotspot, you can:
  - Configure the hotspot to provide DNS / routing to your filtering service (if the system allows).
  - Use a captive-portal-like approach on the hotspot gateway (requires control of the AP/gateway — not possible on platform-managed hotspots).
  - Best cross-platform approach: instruct users to connect to the hotspot and rely on DNS/VPN filtering for content control.

Files added by scaffold

- `android/app/src/main/kotlin/.../MainActivity.kt` — MethodChannel skeleton returning UNIMPLEMENTED.
- `ios/AppDelegate.swift` — Added `pocketfence.hotspot` handler returning UNIMPLEMENTED with guidance.
- `docs/mobile_hotspot_native.md` — This document (native integration guidance).

Next steps

- I can implement a `startLocalOnlyHotspot()` example for Android that requests runtime permissions and returns the generated credentials to Dart.
- I can scaffold a NETunnelProvider sample for iOS (complex and requires adding an extension target and entitlements).

Tell me which native sample you'd like me to implement next: Android LocalOnlyHotspot example, or iOS VPN extension scaffold (or both).