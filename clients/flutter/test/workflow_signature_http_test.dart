import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';

void main() {
  test('signature multipart uses current bearer, locale and PNG content type',
      () async {
    var calls = 0;
    final client = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        locale: 'en-US',
        accessToken: 'old',
        inner: MockClient((request) async {
          calls++;
          expect(request.headers['Authorization'], 'Bearer current');
          expect(request.headers['Accept-Language'], 'en-US');
          expect(request.headers['content-type'],
              startsWith('multipart/form-data; boundary='));
          expect(latin1.decode(request.bodyBytes),
              contains('name="file"; filename="signature.png"'));
          expect(latin1.decode(request.bodyBytes),
              contains('content-type: image/png'));
          return http.Response(
              '{"fileId":"01912345-6789-7abc-8def-0123456789ab"}', 200);
        }));
    client.setAccessToken('current');
    final result = await client.postSignaturePng(
        '/api/v1/workflow/todos/01912345-6789-7abc-8def-0123456789ab/signature-uploads?expectedRevision=1',
        [137, 80, 78, 71, 13, 10, 26, 10]);
    expect(result['fileId'], '01912345-6789-7abc-8def-0123456789ab');
    expect(calls, 1);
    client.close();
  });

  test('absolute upload destinations cannot receive the current credential',
      () async {
    var calls = 0;
    final client = FullNetHttpClient(
        accessToken: 'private',
        inner: MockClient((request) async {
          calls++;
          return http.Response('{}', 200);
        }));
    await expectLater(
        client.postSignaturePng(
            'https://other.example.test/upload', List.filled(8, 0)),
        throwsArgumentError);
    expect(calls, 0);
    client.close();
  });
}
