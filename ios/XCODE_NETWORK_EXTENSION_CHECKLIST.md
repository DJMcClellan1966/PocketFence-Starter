Xcode Network Extension Checklist
===============================

This checklist helps you wire the Packet Tunnel (NETunnelProvider) extension and test DNS provider messages.

1) Create the Packet Tunnel target
   - In Xcode: File → New → Target → Network Extension → "Packet Tunnel"
   - Choose a unique bundle identifier (e.g. com.example.pocketfence.tunnel)

2) Entitlements
   - Extension: add the `Network Extensions` capability and select `Packet Tunnel`.
   - Host app: add any required entitlements (see `ios/Runner-NetworkExt.entitlements` for examples).
   - Ensure the extension has an entitlements file (e.g. `NetworkExtension.entitlements`) assigned.

3) Provisioning
   - Use a provisioning profile that includes the Network Extension entitlement for both the host and extension targets.
   - Match App IDs and provisioning to the bundle identifiers.

4) Add `PacketTunnelProvider.swift` to the extension target
   - Verify target membership: select the file → File Inspector → Target Membership: check only the Packet Tunnel extension.

5) Link frameworks
   - For the extension target, link `NetworkExtension.framework` (Build Phases → Link Binary With Libraries).

6) Update `providerBundleIdentifier` in `ios/Runner/AppDelegate.swift`
   - Replace the placeholder `com.example.pocketfence.tunnel` with your extension bundle id if referenced.

7) Build & Run on a device
   - Packet Tunnel extensions cannot be tested fully on the simulator; use a physical iOS device with developer provisioning.
   - Run the host app; use the `setupVPN` call to save the NETunnelProviderManager configuration.

8) Test provider messaging
   - From the host app, call the MethodChannel `pocketfence.vpn` methods `getDNS` and `setDNS` (or use the helper `lib/vpn_helper.dart`).
   - The extension scaffold responds to `getDNS` and `setDNS` via `handleAppMessage`.

9) Troubleshooting
   - If `NETunnelProviderManager.saveToPreferences` fails: check entitlements, bundle ids, and provisioning profiles.
   - Use device logs (Console.app) and `NSLog` output from both the app and extension for debugging.

10) Useful commands
   - To inspect installed managers on device: run logging in app or use Console.app to view system logs.
