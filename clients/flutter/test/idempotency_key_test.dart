import 'package:flutter_test/flutter_test.dart';

import 'package:fullnet_client/core/idempotency_key.dart';

void main() {
  test('createIdempotencyKey returns uuid-like value', () {
    final value = createIdempotencyKey();
    expect(
      RegExp(
        r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
      ).hasMatch(value),
      isTrue,
    );
  });
}
