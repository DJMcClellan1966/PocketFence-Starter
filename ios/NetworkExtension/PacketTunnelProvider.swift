// Use conditional import so editors that index the app target don't error
#if canImport(NetworkExtension)
import NetworkExtension
#endif

// IMPORTANT: Target membership and framework note
// - Ensure this file is included ONLY in your Network Extension target (Packet Tunnel) in Xcode.
//   If `PacketTunnelProvider.swift` is a member of the main app target, SourceKit/Xcode will attempt
//   to compile it for the app target and you'll see errors like "no such module 'NetworkExtension'".
// - In Xcode: select the file -> File Inspector -> Target Membership -> check only the extension target.
// - Also ensure the Network Extension target links `NetworkExtension.framework` (Build Phases -> Link Binary With Libraries).
// - After changing target membership, clean the build folder and rebuild the extension target to refresh the index.
//
// Minimal Packet Tunnel Provider scaffold for NETunnelProvider extension.
// Add this file to your Network Extension target (Packet Tunnel) in Xcode.
// Configure the extension's bundle identifier and set `providerBundleIdentifier` in AppDelegate.swift.

#if canImport(NetworkExtension)

class PacketTunnelProvider: NEPacketTunnelProvider {

    // Called when the system starts the tunnel extension
    override func startTunnel(options: [String : NSObject]?, completionHandler: @escaping (Error?) -> Void) {
        // Example: Configure VPN/DNS settings here.
        // This scaffold doesn't create a real tunnel; it's a template for implementers.

        // Create a basic tunnel network settings example (adjust as needed):
        let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "127.0.0.1")

        // Example DNS settings — replace with your DNS servers
        let dnsSettings = NEDNSSettings(servers: ["45.90.28.0", "45.90.30.0"])
        settings.dnsSettings = dnsSettings

        // Example IP settings: for a real tunnel, configure IP addressing and routes
        // settings.ipv4Settings = NEIPv4Settings(addresses: ["10.0.0.2"], subnetMasks: ["255.255.255.0"]) 

        setTunnelNetworkSettings(settings) { [weak self] error in
            if let error = error {
                // Failed to set settings — report to host app
                completionHandler(error)
                return
            }

            // Start reading/writing packets via packetFlow if building a real tunnel
            // For example, setup sockets or tun interfaces here and call completionHandler(nil) on success

            // This example completes immediately without a real tunnel
            completionHandler(nil)

            // Optionally, send a message to the host app
            // self?.handleAppMessage(["status": "started"] as [String : Any]) { _ in }
        }
    }

    // Called when the system stops the tunnel extension
    override func stopTunnel(with reason: NEProviderStopReason, completionHandler: @escaping () -> Void) {
        // Clean up sockets/tunnel resources here

        // Indicate the tunnel has been stopped
        completionHandler()
    }

    // Handle messages from the containing app
    override func handleAppMessage(_ messageData: Data, completionHandler: ((Data?) -> Void)?) {
        // Decode messageData and respond if needed
        completionHandler?(nil)
    }

    // Example helper: send status back to host app
    func sendStatusToHost(_ info: [String: Any]) {
        if let msg = try? JSONSerialization.data(withJSONObject: info, options: []) {
            self.sendProviderMessage(msg) { error in
                if let error = error {
                    // handle error
                    NSLog("PacketTunnelProvider: sendProviderMessage error: \(error)")
                }
            }
        }
    }
}

#endif
