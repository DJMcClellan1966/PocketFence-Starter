import 'dart:async';
import 'package:multicast_dns/multicast_dns.dart';

class MDnsDiscovery {
  /// Discover mDNS/Bonjour services for a short duration and return a list
  /// of maps with `name`, `ip`, `port`, `serviceType`.
  static Future<List<Map<String, dynamic>>> discover({Duration timeout = const Duration(seconds: 5)}) async {
    final List<Map<String, dynamic>> results = [];
    final MDnsClient client = MDnsClient();
    try {
      await client.start();
      // Query for _http._tcp.local. as a general example; add other types if needed
      final String service = '_http._tcp';
      final Stream<PtrResourceRecord> ptrs = client.lookup<PtrResourceRecord>(ResourceRecordQuery.serverPointer('$service.local'));
      final completer = Completer<void>();
      final timer = Timer(timeout, () {
        completer.complete();
      });

      ptrs.listen((PtrResourceRecord ptr) async {
        final srvName = ptr.domainName;
        // resolve SRV
        await for (SrvResourceRecord srv in client.lookup<SrvResourceRecord>(ResourceRecordQuery.service(srvName))) {
          final target = srv.target;
          final port = srv.port;
          // resolve A/AAAA
          await for (IPAddressResourceRecord addr in client.lookup<IPAddressResourceRecord>(ResourceRecordQuery.addressIPv4(target))) {
            results.add({
              'name': srvName,
              'ip': addr.address.address,
              'port': port,
              'service': service,
            });
          }
          await for (IPAddressResourceRecord addr6 in client.lookup<IPAddressResourceRecord>(ResourceRecordQuery.addressIPv6(target))) {
            results.add({
              'name': srvName,
              'ip': addr6.address.address,
              'port': port,
              'service': service,
            });
          }
        }
      });

      await completer.future;
      timer.cancel();
    } catch (_) {
      // ignore errors and return what we found
    } finally {
      client.stop();
    }
    return results;
  }
}
