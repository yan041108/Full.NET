import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/core/http/http_problem.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';

const id = '01912345-6789-7abc-8def-0123456789ab';
void main() {
  test('个人附件保留字节并使用当前凭据与语言', () async {
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        accessToken: 'old',
        locale: 'en-US',
        inner: MockClient((request) async {
          expect(request.url.path,
              '/api/v1/workflow/todos/$id/form-attachments/$id/content');
          expect(request.headers['Authorization'], 'Bearer current');
          expect(request.headers['Accept-Language'], 'en-US');
          return http.Response.bytes([137, 80, 78, 71], 200,
              headers: {'content-type': 'image/png'});
        }));
    transport.setAccessToken('current');
    expect(await WorkflowTodoClient(transport).downloadAttachment(id, id),
        [137, 80, 78, 71]);
    transport.close();
  });
  test('二进制错误按实际API顶层机器码返回', () async {
    final transport = FullNetHttpClient(
        inner: MockClient((request) async => http.Response(
            jsonEncode({
              'type': 'about:blank',
              'title': 'Forbidden',
              'status': 403,
              'code': 'workflow.todo.forbidden',
              'traceId': 'trace-attachment'
            }),
            403)));
    await expectLater(
        WorkflowTodoClient(transport).downloadAttachment(id, id),
        throwsA(isA<HttpProblem>()
            .having((problem) => problem.status, 'status', 403)
            .having(
                (problem) => problem.code, 'code', 'workflow.todo.forbidden')
            .having(
                (problem) => problem.traceId, 'traceId', 'trace-attachment')));
    transport.close();
  });
  test('非法标识和外部地址在发送凭据前拒绝', () async {
    var calls = 0;
    final transport = FullNetHttpClient(inner: MockClient((request) async {
      calls++;
      return http.Response('', 200);
    }));
    await expectLater(
        WorkflowTodoClient(transport).downloadAttachment('external', id),
        throwsFormatException);
    await expectLater(
        transport.getBytes('https://foreign.test/file'), throwsArgumentError);
    expect(calls, 0);
    transport.close();
  });
}
