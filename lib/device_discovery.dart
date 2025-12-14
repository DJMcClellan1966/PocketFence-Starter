import 'package:flutter/services.dart';

class DeviceDiscovery {
  static const MethodChannel _channel = MethodChannel('pocketfence.devices');

  /// Returns a list of discovered devices. Each device is a Map<String, dynamic>
  /// with keys: `ip`, `mac`, `name`, `platform`.
  static Future<List<Map<String, dynamic>>> listDevices() async {
    try {
      final res = await _channel.invokeMethod('listDevices');
      if (res is List) {
        return res.map<Map<String, dynamic>>((e) {
          if (e is Map) return Map<String, dynamic>.from(e);
          return <String, dynamic>{};
        }).toList();
      }
    } on PlatformException {
      return <Map<String, dynamic>>[];
    }
    return <Map<String, dynamic>>[];
  }
}
