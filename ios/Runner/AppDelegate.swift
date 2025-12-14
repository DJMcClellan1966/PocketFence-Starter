import Flutter
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    GeneratedPluginRegistrant.register(with: self)
    // Device discovery channel: return simulated list on simulator or empty on device
    let deviceChannel = FlutterMethodChannel(name: "pocketfence.devices", binaryMessenger: self as! FlutterBinaryMessenger)
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
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
}
