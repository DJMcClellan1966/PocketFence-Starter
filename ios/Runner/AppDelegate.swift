import Flutter
import NetworkExtension
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    guard let controller = window?.rootViewController as? FlutterViewController else {
      return super.application(application, didFinishLaunchingWithOptions: launchOptions)
    }

    // VPN channel  sets up an on-demand DNS/VPN tunnel using NetworkExtension.
    let vpnChannel = FlutterMethodChannel(name: "pocketfence.vpn", binaryMessenger: controller.binaryMessenger)

    // Device discovery channel: return simulated list on simulator or empty on device
    let deviceChannel = FlutterMethodChannel(name: "pocketfence.devices", binaryMessenger: controller.binaryMessenger)

    // Hotspot channel (UNIMPLEMENTED on iOS)
    let hotspotChannel = FlutterMethodChannel(name: "pocketfence.hotspot", binaryMessenger: controller.binaryMessenger)

    vpnChannel.setMethodCallHandler { (call: FlutterMethodCall, result: @escaping FlutterResult) -> Void in
      switch call.method {
      case "setupVPN":
        let args = call.arguments as? [String: Any]
        let dnsServers = args? ["dnsServers"] as? [String]
        self.setupDNSVPN(dnsServers: dnsServers) { success in
          result(success)
        }

      case "getDNS":
        self.sendProviderMessageToExtension(["cmd": "getDNS"]) { (data, error) in
          if let error = error {
            result(FlutterError(code: "ERR", message: "sendProviderMessage failed: \(error.localizedDescription)", details: nil))
            return
          }
          guard let data = data else { result(nil); return }
          if let obj = try? JSONSerialization.jsonObject(with: data, options: []), let dict = obj as? [String: Any] {
            result(dict)
            return
          }
          result(nil)
        }

      case "setDNS":
        let args = call.arguments as? [String: Any]
        let dns = args?["dnsServers"] as? [String] ?? []
        self.sendProviderMessageToExtension(["cmd": "setDNS", "dnsServers": dns]) { (data, error) in
          if let error = error {
            result(FlutterError(code: "ERR", message: "sendProviderMessage failed: \(error.localizedDescription)", details: nil))
            return
          }
          if let data = data, let obj = try? JSONSerialization.jsonObject(with: data, options: []), let dict = obj as? [String: Any] {
            result(dict)
            return
          }
          result(nil)
        }

      default:
        result(FlutterMethodNotImplemented)
      }
    }

    deviceChannel.setMethodCallHandler { call, result in
      if call.method == "listDevices" {
        #if targetEnvironment(simulator)
        let devices: [[String: String]] = [
          ["ip": "127.0.0.1", "mac": "00:11:22:33:44:55", "name": "iPhone Simulator", "platform": "ios"]
        ]
        result(devices)
        #else
        result([])
        #endif
      } else {
        result(FlutterMethodNotImplemented)
      }
    }

    hotspotChannel.setMethodCallHandler { (call: FlutterMethodCall, result: @escaping FlutterResult) -> Void in
      switch call.method {
      case "startHotspot", "stopHotspot", "setHotspotName":
        result(FlutterError(code: "UNIMPLEMENTED", message: "Hotspot control is not available on iOS. See docs/mobile_hotspot_native.md.", details: nil))
      default:
        result(FlutterMethodNotImplemented)
      }
    }

    GeneratedPluginRegistrant.register(with: self)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  /// Helper to configure an on-demand NETunnelProviderManager for DNS filtering.
  /// Requires a NETunnelProvider extension target and matching `providerBundleIdentifier`.
  private func setupDNSVPN(dnsServers: [String]?, completion: @escaping (Bool) -> Void) {
    let manager = NETunnelProviderManager()
    let protocolConfig = NETunnelProviderProtocol()
    protocolConfig.serverAddress = "PocketFence DNS"
    protocolConfig.providerBundleIdentifier = "com.example.pocketfence.tunnel" // TODO: replace with your extension bundle id

    let dnsList = dnsServers ?? ["45.90.28.116", "45.90.29.116", "45.90.30.116"]
    protocolConfig.providerConfiguration = ["dnsServers": dnsList]

    let evaluateRule = NEEvaluateConnectionRule(matchDomains: ["*"], andAction: .connectIfNeeded)
    let onDemandRule = NEOnDemandRuleEvaluateConnection()
    onDemandRule.connectionRules = [evaluateRule]
    onDemandRule.interfaceTypeMatch = .any
    manager.onDemandRules = [onDemandRule]
    manager.isOnDemandEnabled = true
    manager.isEnabled = true
    manager.localizedDescription = "PocketFence DNS Filter"

    manager.saveToPreferences { error in
      if let error = error {
        NSLog("NETunnelProviderManager.saveToPreferences failed: \(error.localizedDescription)")
        NSLog("Possible causes: missing Network Extension entitlement, mismatched providerBundleIdentifier, or invalid provisioning profile.")
        completion(false)
        return
      }
      Task {
        do {
          try await manager.loadFromPreferences()
          completion(true)
        } catch {
          NSLog("Failed to load NETunnelProviderManager preferences after save: \(error)")
          completion(false)
        }
      }
    }
  }

  /// Helper to send JSON provider messages to the NETunnelProvider extension.
  /// The extension must be running/connected; this loads saved managers and uses the first NETunnelProviderManager found.
  private func sendProviderMessageToExtension(_ json: [String: Any], completion: @escaping (Data?, Error?) -> Void) {
    guard let data = try? JSONSerialization.data(withJSONObject: json, options: []) else {
      completion(nil, NSError(domain: "AppDelegate", code: 1, userInfo: [NSLocalizedDescriptionKey: "Invalid JSON payload"]))
      return
    }

    NETunnelProviderManager.loadAllFromPreferences { managers, error in
      if let error = error {
        completion(nil, error)
        return
      }

      guard let managers = managers, let manager = managers.first(where: { $0.protocolConfiguration is NETunnelProviderProtocol }) else {
        completion(nil, NSError(domain: "AppDelegate", code: 2, userInfo: [NSLocalizedDescriptionKey: "No NETunnelProviderManager found"]))
        return
      }

      let connection = manager.connection

      if let session = connection as? NETunnelProviderSession {
        try? session.sendProviderMessage(data) { responseData in
          completion(responseData, nil)
        }
      } else {
        completion(nil, NSError(domain: "AppDelegate", code: 4, userInfo: [NSLocalizedDescriptionKey: "Connection is not a NETunnelProviderSession"]))
      }
    }
  }
}
