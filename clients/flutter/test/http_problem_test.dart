import 'package:flutter_test/flutter_test.dart';

import 'package:fullnet_client/core/http/http_problem.dart';

void main() {
  test('tryParseHttpProblem reads stable error code from extensions', () {
    final problem = tryParseHttpProblem(403, {
      'type': 'about:blank',
      'title': 'Forbidden',
      'extensions': {
        'code': 'authorization.permission_denied',
        'traceId': 'trace-1',
      },
    });

    expect(problem, isNotNull);
    expect(problem!.status, 403);
    expect(problem.code, 'authorization.permission_denied');
    expect(problem.traceId, 'trace-1');
  });
}
