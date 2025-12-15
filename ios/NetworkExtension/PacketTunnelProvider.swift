// Use conditional import so editors that index the app target don't error
#if canImport(NetworkExtension)
import NetworkExtension

// IMPORTANT: Include this file only in the Network Extension (Packet Tunnel) target.
// - In Xcode: File Inspector -> Target Membership -> select only the extension target.
// - Link NetworkExtension.framework in the extension target (Build Phases -> Link Binary With Libraries).
// - Ensure entitlements/provisioning are configured for Network Extension capability.

/// Minimal PacketTunnelProvider scaffold with basic DNS handling and AppMessage commands.
class PacketTunnelProvider: NEPacketTunnelProvider {

    // Gracefully parse DNS list from providerConfiguration.
    private func dnsListFromConfiguration() -> [String] {
        if let cfg = self.protocolConfiguration?.providerConfiguration as? [String: Any],
           let provided = cfg["dnsServers"] as? [String],
           provided.count > 0 {
            return provided
        }
        return ["45.90.28.116", "45.90.29.116", "45.90.30.116"]
    }

    // Called when the system starts the tunnel extension
    override func startTunnel(options: [String : NSObject]?, completionHandler: @escaping (Error?) -> Void) {
        NSLog("PacketTunnelProvider: startTunnel called")

        let settings = NEPacketTunnelNetworkSettings(tunnelRemoteAddress: "127.0.0.1")

        let dnsServers = dnsListFromConfiguration()
        NSLog("PacketTunnelProvider: applying DNS servers: \(dnsServers)")

        let dnsSettings = NEDNSSettings(servers: dnsServers)
        settings.dnsSettings = dnsSettings

        // NOTE: For a functional packet tunnel, configure IP addressing and routes here.
        setTunnelNetworkSettings(settings) { error in
            if let error = error {
                NSLog("PacketTunnelProvider: failed to set tunnel settings: \(error)")
                completionHandler(error)
                return
            }
            NSLog("PacketTunnelProvider: tunnel network settings applied")
            completionHandler(nil)
        }
    }

    // Called when the system stops the tunnel extension
    override func stopTunnel(with reason: NEProviderStopReason, completionHandler: @escaping () -> Void) {
        NSLog("PacketTunnelProvider: stopTunnel called, reason: \(reason)")
        // Tear down sockets or resources here
        completionHandler()
    }

    // Handle messages sent from the host app via sendProviderMessage
    // Expected JSON payloads (UTF-8 encoded):
    // - { "cmd": "getDNS" }
    // - { "cmd": "setDNS", "dnsServers": ["1.2.3.4"] }
    override func handleAppMessage(_ messageData: Data, completionHandler: ((Data?) -> Void)?) {
        guard let json = try? JSONSerialization.jsonObject(with: messageData, options: []),
              let dict = json as? [String: Any],
              let cmd = dict["cmd"] as? String else {
            NSLog("PacketTunnelProvider: handleAppMessage - invalid message")
            completionHandler?(nil)
            return
        }

        switch cmd {
        case "getDNS":
            let dns = dnsListFromConfiguration()
            let resp: [String: Any] = ["dnsServers": dns]
            if let data = try? JSONSerialization.data(withJSONObject: resp, options: []) {
                completionHandler?(data)
            } else {
                completionHandler?(nil)
            }

        case "setDNS":
            if let newDns = dict["dnsServers"] as? [String], newDns.count > 0 {
                // Update providerConfiguration so future startTunnel sees the new DNS list.
                if var proto = self.protocolConfiguration as? NETunnelProviderProtocol {
                    var cfg = proto.providerConfiguration ?? [:]
                    cfg["dnsServers"] = newDns
                    proto.providerConfiguration = cfg
                    self.protocolConfiguration = proto
                    NSLog("PacketTunnelProvider: providerConfiguration updated with new DNS: \(newDns)")
                }
                let resp: [String: Any] = ["result": "ok"]
                completionHandler?(try? JSONSerialization.data(withJSONObject: resp, options: []))
            } else {
                let resp: [String: Any] = ["error": "invalid dnsServers"]
                completionHandler?(try? JSONSerialization.data(withJSONObject: resp, options: []))
            }

        default:
            NSLog("PacketTunnelProvider: handleAppMessage - unknown cmd: \(cmd)")
            completionHandler?(nil)
        }
    }

    // Example helper: send status back to host app
    func sendStatusToHost(_ info: [String: Any]) {
        if let msg = try? JSONSerialization.data(withJSONObject: info, options: []) {
            self.sendProviderMessage(msg) { error in
                if let error = error {
                    NSLog("PacketTunnelProvider: sendProviderMessage error: \(error)")
                }
            }
        }
    }
}

#endif
