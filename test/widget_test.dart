// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter_test/flutter_test.dart';

import 'package:pocketfence/main.dart';

void main() {
  testWidgets('App builds and shows dashboard', (WidgetTester tester) async {
    // Build the app.
    await tester.pumpWidget(const PocketFenceApp());
    await tester.pumpAndSettle();

    // Verify key UI is present (updated UI labels).
    expect(find.text('PocketFence Hotspot'), findsOneWidget);
    expect(find.textContaining('Status:'), findsOneWidget);
  });
}
