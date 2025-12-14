import 'package:flutter/services.dart';
import 'mdns_discovery.dart';

class DeviceDiscovery {
  static const MethodChannel _channel = MethodChannel('pocketfence.devices');

  /// Returns a list of discovered devices. Each device is a Map&lt;String, dynamic&gt;
  /// with keys: `ip`, `mac`, `name`, `platform`.
  static Future<List<Map<String, dynamic>>> listDevices() async {
    final List<Map<String, dynamic>> results = [];
    // First, try mDNS discovery (cross-platform)
    try {
      final mdns = await MDnsDiscovery.discover();
      for (var m in mdns) {
        results.add(Map<String, dynamic>.from(m));
      }
    } catch (_) {}

    // Then, ask the platform native layer (ARP parsing / platform-specific)
    try {
      final res = await _channel.invokeMethod('listDevices');
      if (res is List) {
        for (var e in res) {
          if (e is Map) results.add(Map<String, dynamic>.from(e));
        }
      }
    } on PlatformException {
      // ignore
    }

    // Deduplicate by ip/mac
    final seen = <String>{};
    final deduped = <Map<String, dynamic>>[];
    for (var d in results) {
      final key = (d['mac'] ?? d['ip'] ?? d['name'] ?? '').toString();
      if (key.isEmpty) continue;
      if (!seen.contains(key)) {
        seen.add(key);
        deduped.add(d);
      }
    }
    return deduped;
  }
}
