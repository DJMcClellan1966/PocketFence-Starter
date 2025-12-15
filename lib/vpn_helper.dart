import 'package:flutter/services.dart';

class VpnHelper {
  static const MethodChannel _channel = MethodChannel('pocketfence.vpn');

  /// Request the current DNS servers from the NETunnelProvider extension.
  /// Returns a List&lt;String&gt; of DNS server IPs or null on failure.
  static Future<List<String>?> getDNS() async {
    try {
      final res = await _channel.invokeMethod('getDNS');
      if (res == null) return null;
      if (res is List) return res.cast<String>();
      if (res is Map && res['dnsServers'] is List) return List<String>.from(res['dnsServers']);
      return null;
    } on PlatformException {
      return null;
    }
  }

  /// Send a new DNS server list to the NETunnelProvider extension.
  /// Returns true on success.
  static Future<bool> setDNS(List<String> dnsServers) async {
    try {
      final res = await _channel.invokeMethod('setDNS', {'dnsServers': dnsServers});
      if (res == null) return false;
      if (res is bool) return res;
      if (res is Map && res['result'] == 'ok') return true;
      return false;
    } on PlatformException {
      return false;
    }
  }
}
