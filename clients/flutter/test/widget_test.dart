import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fullnet_client/app/full_net_app.dart';
import 'package:fullnet_client/features/auth/login_page.dart';

void main() {
  testWidgets('桌面入口加载真实登录表单并遮蔽密码', (tester) async {
    await tester.pumpWidget(const FullNetApp());
    await tester.pumpAndSettle();
    expect(find.byType(LoginPage), findsOneWidget);
    final fields = tester.widgetList<TextField>(find.byType(TextField)).toList();
    expect(fields, hasLength(2));
    expect(fields.last.obscureText, isTrue);
    expect(find.byType(FilledButton), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
  });
}
