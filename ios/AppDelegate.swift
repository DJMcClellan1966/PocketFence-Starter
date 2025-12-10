import Flutter
import NetworkExtension
import UIKit

@UIApplicationMain
@objc class AppDelegate: FlutterAppDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    let controller : FlutterViewController = window?.rootViewController as! FlutterViewController
    let vpnChannel = FlutterMethodChannel(name: "pocketfence.vpn", binaryMessenger: controller.binaryMessenger)

    vpnChannel.setMethodCallHandler { (call: FlutterMethodCall, result: @escaping FlutterResult) -> Void in
      if call.method == "setupVPN" {
        self.setupDNSVPN { success in
          result(success)
        }
      } else {
        result(FlutterMethodNotImplemented)
      }
    }

    GeneratedPluginRegistrant.register(with: self)
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  private func setupDNSVPN(completion: @escaping (Bool) -> Void) {
    let manager = NETunnelProviderManager()
    let protocolConfig = NETunnelProviderProtocol()
    protocolConfig.serverAddress = "NextDNS VPN"
    protocolConfig.providerBundleIdentifier = "com.yourcompany.pocketfence.tunnel" // Add extension target
    let dnsSettings = NEDNSSettings(servers: ["45.90.28.0", "45.90.30.0"])
    protocolConfig.providerConfiguration = ["dns": dnsSettings] // Custom config for DNS

    let evaluateRule = NEEvaluateConnectionRule(matchDomains: ["*"], andAction: .connectIfNeeded)
    let onDemandRule = NEOnDemandRuleEvaluateConnection(connectionRules: [evaluateRule], interfaceTypeMatch: .any)
    manager.onDemandRules = [onDemandRule]
    manager.isOnDemandEnabled = true
    manager.isEnabled = true
    manager.localizedDescription = "PocketFence DNS Filter"
    manager.saveToPreferences { error in
      if error == nil {
        try? manager.loadFromPreferences()
        completion(true)
      } else {
        completion(false)
      }
    }
  }
}