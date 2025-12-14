import 'dart:async';
import 'package:flutter/services.dart';

class HotspotService {
  static const MethodChannel _channel = MethodChannel('pocketfence.hotspot');

  /// Starts the platform hotspot with the provided options.
  /// Returns a map with platform-specific result data.
  static Future<Map<String, dynamic>?> startHotspot({
    required String clientName,
    required bool blockOthers,
    List<String>? dnsServers,
  }) async {
    final args = <String, dynamic>{
      'ssid': clientName,
      'blockOthers': blockOthers,
      'dnsServers': dnsServers ?? [],
    };
    final result = await _channel.invokeMethod('startHotspot', args);
    if (result is Map) return Map<String, dynamic>.from(result);
    return null;
  }

  /// Stops the platform hotspot.
  static Future<bool> stopHotspot() async {
    final result = await _channel.invokeMethod('stopHotspot');
    if (result is bool) return result;
    return true;
  }
}
