import 'package:flutter_test/flutter_test.dart';

import 'package:fullnet_client/design_system/full_net_theme.dart';

void main() {
  test('buildFullNetTheme enables Material 3', () {
    final theme = buildFullNetTheme(brightness: Brightness.light);
    expect(theme.useMaterial3, isTrue);
    expect(theme.colorScheme?.primary, isNotNull);
  });
}
