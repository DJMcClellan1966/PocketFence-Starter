iOS Network Extension setup for PocketFence

Overview

This project includes a scaffolded `setupDNSVPN` implementation in `AppDelegate.swift` that expects a NETunnelProvider extension.
iOS does not allow apps to programmatically enable or rename the OS Personal Hotspot; the Network Extension (NETunnelProvider)
approach is for VPN/DNS filtering only and requires additional Xcode/provisioning work.

This README explains the manual steps to add a NETunnelProvider extension, configure entitlements, set the correct
`providerBundleIdentifier`, and test on a physical device.

Important notes
- Do NOT expect NETunnelProvider or Personal Hotspot programmatic control to work on the Simulator.
- A physical device and proper provisioning/profile are required for NETunnelProvider features.
- Some Network Extension entitlements require Apple approval for App Store distribution.

Steps to add a NETunnelProvider extension

1. Open the iOS Xcode workspace (Runner.xcworkspace) for this Flutter project.

2. Add a new target:
   - File > New > Target...
   - Choose `Network Extension` > `Packet Tunnel Provider` or `App Proxy Provider` depending on requirement. For DNS filtering, a `Packet Tunnel Provider` (NETunnelProvider) is often used.
   - Give it an identifier such as: `com.yourcompany.pocketfence.tunnel`.
   - Xcode will create a new target and a template Swift file for the extension.

3. Configure entitlements and capabilities:
   - Select the main app target and the extension target. In the `Signing & Capabilities` tab:
     - Add `Network Extensions` capability for both targets as needed.
     - For the extension, enable the appropriate extension points (e.g. `NETunnelProvider`).
   - Update App IDs and provisioning profiles in the Apple Developer portal to include the Network Extension capability.

4. Update `AppDelegate.swift`:
   - Edit `providerBundleIdentifier` in `setupDNSVPN` to match the extension's bundle id. For example:
     ```swift
     protocolConfig.providerBundleIdentifier = "com.yourcompany.pocketfence.tunnel"
     ```

5. Add the entitlements file(s):
   - Create or confirm `.entitlements` files for the app and extension that include the `com.apple.developer.networking.networkextension` key.
   - Example keys that may be required (your exact keys depend on extension type):
     - `com.apple.developer.networking.networkextension`: an array of strings, e.g. ["packet-tunnel"]
     - `com.apple.developer.networking.vpn.api`: true (for certain VPN APIs)

6. Provisioning and signing:
   - Create App IDs for both main app and extension, enable Network Extensions capability in the developer portal.
   - Create or update provisioning profiles for the app and the extension using those App IDs.
   - Use those provisioning profiles in Xcode for the app and extension targets.

7. Run on a device:
   - Build and run the main app on a physical iOS device connected to Xcode.
   - Ensure the extension appears and is signed correctly. Call the `setupVPN` method from the Flutter UI and watch Xcode logs for success/failure.

```markdown
## iOS Network Extension setup for PocketFence

Overview

This project includes a scaffolded `setupDNSVPN` implementation in `AppDelegate.swift` that expects a NETunnelProvider extension.
iOS does not allow apps to programmatically enable or rename the OS Personal Hotspot; the Network Extension (NETunnelProvider)
approach is for VPN/DNS filtering only and requires additional Xcode/provisioning work.

This README explains the manual steps to add a NETunnelProvider extension, configure entitlements, set the correct
`providerBundleIdentifier`, and test on a physical device.

Important notes
- Do NOT expect NETunnelProvider or Personal Hotspot programmatic control to work on the Simulator.
- A physical device and proper provisioning/profile are required for NETunnelProvider features.
- Some Network Extension entitlements require Apple approval for App Store distribution.

Steps to add a NETunnelProvider extension

1. Open the iOS Xcode workspace (`Runner.xcworkspace`) for this Flutter project.

2. Add a new target:
   - File → New → Target...
   - Choose `Network Extension` → `Packet Tunnel Provider` (or `App Proxy Provider` if more appropriate).
   - Give it a unique bundle identifier, for example: `com.yourcompany.pocketfence.tunnel`.
   - Xcode will create a new target and a template Swift file for the extension.

3. Target membership & SourceKit note (very important)
   - Ensure `PacketTunnelProvider.swift` (or the template Swift file) is a member ONLY of the Network Extension target.
     If the file is included in the main app target, SourceKit and Xcode may attempt to compile it for the app and you'll see
     errors like `no such module 'NetworkExtension'`.
   - In Xcode: select the file → File Inspector → Target Membership → check only the extension target and uncheck the main app target.
   - Ensure the Network Extension target links `NetworkExtension.framework` (Build Phases → Link Binary With Libraries).

4. Configure entitlements and capabilities:
   - Select both the main app target and the extension target. In the `Signing & Capabilities` tab:
     - Add `Network Extensions` capability as needed.
     - For the extension, enable the appropriate extension point (e.g. `NETunnelProvider`).
   - Update App IDs and provisioning profiles in the Apple Developer portal to include the Network Extension capability.

5. Update `AppDelegate.swift`:
   - Set `providerBundleIdentifier` in `setupDNSVPN` to match the extension's bundle id. For example:
     ```swift
     protocolConfig.providerBundleIdentifier = "com.yourcompany.pocketfence.tunnel"
     ```
   - We added a placeholder in the repo: `com.example.pocketfence.tunnel`. Replace it with your real extension id.

6. Add or update the entitlements file(s):
   - Create or confirm `.entitlements` files for the app and extension that include the `com.apple.developer.networking.networkextension` key.
   - Example values (adjust per Apple docs):
     - `com.apple.developer.networking.networkextension`: ["packet-tunnel"]
     - `com.apple.developer.networking.vpn.api`: true (if required)

7. Provisioning and signing:
   - Create App IDs for both the app and extension in the Apple Developer portal and enable Network Extensions capability.
   - Create provisioning profiles that include the Network Extension entitlement for both targets and use them in Xcode.

8. Build & run on a physical device:
   - Select the main app scheme and run on a physical device via Xcode.
   - Confirm the extension is bundled and signed; call `setupVPN` from the app and monitor Xcode/device logs for `NETunnelProvider` activity.

Testing and debugging tips
- Use Console.app or Xcode device logs for extension lifecycle and error messages.
- If `manager.saveToPreferences` fails, verify entitlements, provisioning and bundle identifiers carefully.
- Clean the build folder and delete DerivedData when switching target membership or entitlements to refresh Xcode's index.

Files in this repo that help
- `ios/AppDelegate.swift` contains `setupDNSVPN` and a placeholder `providerBundleIdentifier` value.
- `ios/NetworkExtension/PacketTunnelProvider.swift` is a scaffold. Make sure it's only in the extension target per step 3.
- Example entitlements are provided in `ios/NetworkExtension/*.entitlements` — adapt them in Xcode as needed.

Further reading
- Network Extension Programming Guide: https://developer.apple.com/documentation/networkextension
- NETunnelProvider: https://developer.apple.com/documentation/networkextension/netunnelprovider

If you'd like, I can add a minimal `NetworkExtension.entitlements` template to the repo or generate a short Xcode checklist you can paste into your project. Which would you prefer?
 
Example App IDs & provisioning profile names (suggested)
-----------------------------------------------
- Main app (Runner)
   - App ID: com.example.pocketfence
   - Provisioning profile name: PocketFence App Profile (com.example.pocketfence)

- Network Extension (Packet Tunnel)
   - App ID: com.example.pocketfence.tunnel
   - Provisioning profile name: PocketFence Tunnel Profile (com.example.pocketfence.tunnel)

Notes
- In the Apple Developer portal create two App IDs matching the entries above and enable the Network Extensions capability for the tunnel App ID.
- Create provisioning profiles for both App IDs and download/install them in Xcode (or use automatic signing after enabling capabilities).
- Ensure `providerBundleIdentifier` in `ios/AppDelegate.swift` matches the tunnel App ID exactly.

Done — these are example names to help you create the necessary profiles and App IDs in the Apple Developer portal.
```