import Flutter
import NetworkExtension
import UIKit

// This file is preserved as a reference. The active AppDelegate is ios/Runner/AppDelegate.swift
// The original AppDelegate entry annotation is commented out to avoid duplicate symbols.
@objc class AppDelegate_Old: FlutterAppDelegate {
  /**
   iOS notes
   - iOS does not allow third-party apps to enable or rename the system "Personal Hotspot" programmatically.
   - Some hotspot-like functionality can be achieved with NEHotspotConfiguration (connect to networks) or
     using NetworkExtension frameworks for privileged extensions; both require App Store entitlements
     or a provisioning profile and cannot fully replace the system Personal Hotspot UI.
   - For these reasons the `pocketfence.hotspot` channel is UNIMPLEMENTED on iOS and returns an error.
   */
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    guard let controller = window?.rootViewController as? FlutterViewController else {
      return super.application(application, didFinishLaunchingWithOptions: launchOptions)
    }

    // VPN channel (implemented) — sets up an on-demand DNS/VPN tunnel using NetworkExtension.
    let vpnChannel = FlutterMethodChannel(name: "pocketfence.vpn", binaryMessenger: controller.binaryMessenger)

    // Hotspot channel (UNIMPLEMENTED on iOS) — provide guidance to implementers.
    let hotspotChannel = FlutterMethodChannel(name: "pocketfence.hotspot", binaryMessenger: controller.binaryMessenger)

    vpnChannel.setMethodCallHandler { (call: FlutterMethodCall, result: @escaping FlutterResult) -> Void in
      if call.method == "setupVPN" {
        // Accept optional DNS servers from Dart via call.arguments
        let args = call.arguments as? [String: Any]
        let dnsServers = args?["dnsServers"] as? [String]
        // The DNS/VPN setup uses NETunnelProviderManager and requires a NetworkExtension target.
        // Ensure you create an extension target and set the `providerBundleIdentifier` appropriately.
        self.setupDNSVPN(dnsServers: dnsServers) { success in
          result(success)
        }
      } else {
        result(FlutterMethodNotImplemented)
      }
    }

    hotspotChannel.setMethodCallHandler { (call: FlutterMethodCall, result: @escaping FlutterResult) -> Void in
      switch call.method {
      case "startHotspot", "stopHotspot", "setHotspotName":
        // Return a clear UNIMPLEMENTED error with a pointer to the docs.
        result(FlutterError(code: "UNIMPLEMENTED", message: "Hotspot control is not available on iOS. See docs/mobile_hotspot_native.md for details and possible alternatives.", details: nil))
      default:
        result(FlutterMethodNotImplemented)
      }
    }

    GeneratedPluginRegistrant.register(with: self)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  /**
   Helper to configure an on-demand NETunnelProviderManager for DNS filtering.
   - This requires a NETunnelProvider extension target and matching `providerBundleIdentifier`.
   - For production usage you must configure entitlements, provisioning, and ensure the extension is included.
  */
  private func setupDNSVPN(dnsServers: [String]?, completion: @escaping (Bool) -> Void) {
    let manager = NETunnelProviderManager()
    let protocolConfig = NETunnelProviderProtocol()
    protocolConfig.serverAddress = "NextDNS VPN"
    // Replace the string below with the actual bundle identifier of your Packet Tunnel
    // extension target (for example: "com.example.pocketfence.tunnel").
    // To find the bundle identifier: open the Network Extension target in Xcode and check
    // the "Bundle Identifier" field in the target's General settings.
    //
    // Entitlements files in this repository (templates):
    // - ios/NetworkExtension/NetworkExtension.entitlements  (for the extension)
    // - ios/Runner-NetworkExt.entitlements                  (for the host app when needed)
    // In Xcode, assign the appropriate entitlements file to each target under
    // Signing & Capabilities → Entitlements File. Also ensure provisioning profiles
    // include the Network Extension capability.
    protocolConfig.providerBundleIdentifier = "com.example.pocketfence.tunnel" // TODO: replace with your NETunnelProvider extension bundle id

    // DNS settings: prefer DNS servers passed from Dart, otherwise fall back to defaults
    let dnsList = dnsServers ?? ["45.90.28.116", "45.90.29.116", "45.90.30.116"]
    // Store DNS servers in providerConfiguration so the extension can read and apply them at runtime.
    protocolConfig.providerConfiguration = ["dnsServers": dnsList]

    // On-demand rule: attempt to connect when network access is requested.
    let evaluateRule = NEEvaluateConnectionRule(matchDomains: ["*"], andAction: .connectIfNeeded)
    let onDemandRule = NEOnDemandRuleEvaluateConnection(connectionRules: [evaluateRule], interfaceTypeMatch: .any)
    manager.onDemandRules = [onDemandRule]
    manager.isOnDemandEnabled = true
    manager.isEnabled = true
    manager.localizedDescription = "PocketFence DNS Filter"

    manager.saveToPreferences { error in
      if let error = error {
        // Provide actionable log output to help debugging entitlement/provisioning issues.
        NSLog("NETunnelProviderManager.saveToPreferences failed: \(error.localizedDescription)")
        NSLog("Possible causes: missing Network Extension entitlement, mismatched providerBundleIdentifier, or invalid provisioning profile.")
        NSLog("Check entitlements: ios/NetworkExtension/NetworkExtension.entitlements and ios/Runner-NetworkExt.entitlements, and ensure they are assigned to the correct targets in Xcode.")
        completion(false)
        return
      }

      // Success — attempt to load saved configuration for verification
      do {
        try manager.loadFromPreferences()
        completion(true)
      } catch {
        NSLog("Failed to load NETunnelProviderManager preferences after save: \(error)")
        completion(false)
      }
    }
  }
}