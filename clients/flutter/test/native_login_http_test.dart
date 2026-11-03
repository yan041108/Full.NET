import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';

void main() {
  test('原生登录使用配置API的精确Origin，后续本人读取携带新令牌', () async {
    final requests = <http.Request>[];
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test:8443',
        inner: MockClient((request) async {
          requests.add(request);
          if (request.url.path.endsWith('/login')) {
            expect(request.headers['Origin'], 'https://api.example.test:8443');
            expect(request.headers['Authorization'], isNull);
            return http.Response(jsonEncode({'accessToken': 'native-token'}), 200);
          }
          expect(request.headers['Authorization'], 'Bearer native-token');
          return http.Response(jsonEncode({
            'id': '01912345-6789-7abc-8def-0123456789ab',
            'username': 'native',
            'displayName': 'Native',
            'preferredLocale': 'en-US',
            'profileVersion': 1,
            'permissions': ['workflow.todos.read'],
          }), 200);
        }));
    final session = IdentitySession(transport);
    await session.login(username: 'native', password: 'fixture-password');
    expect(session.isAuthenticated, isTrue);
    expect(requests.map((request) => request.url.path),
        ['/api/v1/auth/login', '/api/v1/me']);
    session.dispose();
    transport.close();
  });
}
