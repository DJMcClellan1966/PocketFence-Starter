import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pocketfence/main.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const MethodChannel channel = MethodChannel('pocketfence.vpn');

  setUp(() {
    TestWidgetsFlutterBinding.ensureInitialized();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, (MethodCall call) async {
      if (call.method == 'getDNS') return {'dnsServers': ['1.1.1.1']};
      if (call.method == 'setDNS') return {'result': 'ok'};
      return null;
    });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger.setMockMethodCallHandler(channel, null);
  });

  testWidgets('VPN UI buttons work and dialogs show', (WidgetTester tester) async {
    await tester.pumpWidget(MaterialApp(home: const PocketFenceApp()));

    expect(find.text('Get DNS'), findsOneWidget);
    expect(find.text('Set DNS'), findsOneWidget);

    // Tap Get DNS
    await tester.tap(find.text('Get DNS'));
    await tester.pumpAndSettle();
    expect(find.text('Current DNS'), findsOneWidget);
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();

    // Tap Set DNS
    await tester.tap(find.text('Set DNS'));
    await tester.pumpAndSettle();
    expect(find.text('Set DNS Servers'), findsOneWidget);
    final dialog = find.byType(AlertDialog);
    expect(dialog, findsOneWidget);
    final tf = find.descendant(of: dialog, matching: find.byType(TextField));
    expect(tf, findsOneWidget);
    await tester.enterText(tf, '9.9.9.9');
    await tester.tap(find.text('Set'));
    await tester.pumpAndSettle();
    expect(find.text('Success'), findsOneWidget);
  });
}
