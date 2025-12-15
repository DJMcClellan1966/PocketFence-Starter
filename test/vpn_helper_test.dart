import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pocketfence/vpn_helper.dart';

void main() {
  const MethodChannel channel = MethodChannel('pocketfence.vpn');
  TestWidgetsFlutterBinding.ensureInitialized();

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, null);
  });

  test('getDNS returns list from channel map', () async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, (MethodCall call) async {
      if (call.method == 'getDNS') return {'dnsServers': ['1.1.1.1', '8.8.8.8']};
      return null;
    });

    final dns = await VpnHelper.getDNS();
    expect(dns, isNotNull);
    expect(dns, contains('1.1.1.1'));
  });

  test('setDNS returns success when extension returns map', () async {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, (MethodCall call) async {
      if (call.method == 'setDNS') return {'result': 'ok'};
      return null;
    });

    final ok = await VpnHelper.setDNS(['9.9.9.9']);
    expect(ok, isTrue);
  });
}
