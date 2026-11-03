import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_detail_page.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';
import 'workflow_form_files_test.dart' as fixtures;

const id = '01912345-6789-7abc-8def-0123456789ab';
http.Response json(Object value, [int status = 200]) =>
    http.Response(jsonEncode(value), status,
        headers: {'content-type': 'application/json'});
Map<String, dynamic> runtime() => {
      'id': id,
      'instanceId': id,
      'stepId': id,
      'assigneeUserId': id,
      'statusKey': 'active',
      'revision': 3,
      'submissionRevision': 4,
      'activityKindKey': 'human.approval',
      'allowedActions': ['approve'],
      'signRequired': false,
      'commentRequired': false,
      'formSchema': fixtures.schema(),
      'fieldPolicies': {'input': 'readOnly'},
      'submission': {
        'input': [id],
        'rows': [
          {'title': 'sensitive row'}
        ]
      },
      'attachmentNames': {id: 'source-owned.png'}
    };
Map<String, dynamic> user() => {
      'id': id,
      'username': 'member',
      'displayName': 'Member',
      'preferredLocale': 'en-US',
      'profileVersion': 1,
      'permissions': ['workflow.todos.read', 'workflow.todos.approve']
    };
Widget app(IdentitySession session, WorkflowTodoClient client) => MaterialApp(
    locale: const Locale('en', 'US'),
    localizationsDelegates: const [AppLocalizations.delegate],
    supportedLocales: AppLocalizations.supportedLocales,
    home: Builder(
        builder: (context) => Scaffold(
            body: TextButton(
                onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                        builder: (_) => WorkflowTodoDetailPage(
                            todoId: id, session: session, client: client))),
                child: const Text('Open task')))));

void main() {
  testWidgets('身份变化关闭已打开的附件预览，不留下旧图像', (tester) async {
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') return json(user());
          if (request.url.path.endsWith('/runtime')) return json(runtime());
          return http.Response.bytes(
              base64Decode(
                  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII='),
              200,
              headers: {'content-type': 'image/png'});
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(session, WorkflowTodoClient(transport)));
    await tester.tap(find.text('Open task'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('source-owned.png'));
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    session.logout();
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsNothing);
    expect(find.byType(Image), findsNothing);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  }, variant: const TargetPlatformVariant({TargetPlatform.android}));
  testWidgets('主表单草稿未保存时关闭代理交接，不丢弃新附件或明细', (tester) async {
    var writes = 0;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return json({
              ...user(),
              'permissions': [
                'workflow.todos.read',
                'workflow.todos.approve',
                'workflow.todos.delegate'
              ]
            });
          }
          if (request.url.path.endsWith('/runtime'))
            return json({
              ...runtime(),
              'allowedActions': ['approve', 'delegate']
            });
          if (request.url.path.endsWith('/delegation'))
            return json({'delegation': null});
          if (request.url.path.endsWith('/delegation-candidates'))
            return json({
              'items': [
                {
                  'id': '01912345-6789-7abc-8def-0123456789ac',
                  'username': 'proxy',
                  'displayName': 'Proxy'
                }
              ],
              'page': 1,
              'pageSize': 20,
              'total': 1
            });
          writes++;
          return json(<String, dynamic>{});
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(session, WorkflowTodoClient(transport)));
    await tester.tap(find.text('Open task'));
    await tester.pumpAndSettle();
    final candidates = find.byType(DropdownButtonFormField<String>);
    await tester.scrollUntilVisible(candidates, 200,
        scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();
    await tester.tap(candidates);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Proxy').last);
    await tester.pumpAndSettle();
    final delegate = find.byKey(const ValueKey('workflow-delegate'));
    expect(tester.widget<OutlinedButton>(delegate).onPressed, isNotNull);
    await tester.scrollUntilVisible(find.byKey(const ValueKey('title')), -200,
        scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const ValueKey('title')), 'unsaved row');
    await tester.pumpAndSettle();
    FocusManager.instance.primaryFocus?.unfocus();
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(delegate, 200,
        scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();
    expect(tester.widget<OutlinedButton>(delegate).onPressed, isNull);
    expect(
        find.text(
            'Handoff does not submit fields. Restore unsaved changes first.'),
        findsOneWidget);
    expect(writes, 0);
    await tester.scrollUntilVisible(find.byKey(const ValueKey('title')), -200,
        scrollable: find.byType(Scrollable).first);
    await tester.pumpAndSettle();
    expect(find.text('unsaved row'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
  for (final denied in [false, true]) {
    testWidgets('实际页面提交可写附件/明细Patch并在拒绝时销毁投影: $denied', (tester) async {
      await tester.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final bodies = <Map<String, dynamic>>[];
      final transport = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') return json(user());
            if (request.url.path.endsWith('/runtime')) return json(runtime());
            expect(request.url.path, '/api/v1/workflow/todos/$id/approve');
            final body = jsonDecode(request.body) as Map<String, dynamic>;
            bodies.add(body);
            expect(body['fieldPatch'], {
              'output': [id],
              'rows': [
                {'title': 'result row'}
              ]
            });
            expect(body['expectedRevision'], 3);
            expect(body['expectedFormRevision'], 4);
            return denied
                ? json({
                    'type': 'about:blank',
                    'title': 'Forbidden',
                    'status': 403,
                    'code': 'workflow.todo.forbidden'
                  }, 403)
                : json(<String, dynamic>{});
          }));
      final session = IdentitySession(transport);
      await session.refreshCurrentUser();
      await tester.pumpWidget(app(session, WorkflowTodoClient(transport)));
      await tester.tap(find.text('Open task'));
      await tester.pumpAndSettle();
      final reuse = find.text('Reuse visible attachment: source-owned.png');
      await tester.ensureVisible(reuse);
      await tester.tap(reuse);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const ValueKey('title')));
      await tester.enterText(find.byKey(const ValueKey('title')), 'result row');
      await tester.pump();
      await tester.ensureVisible(find.text('Approve'));
      await tester.tap(find.text('Approve'));
      await tester.pumpAndSettle();
      expect(bodies, hasLength(1));
      if (denied) {
        expect(find.text('source-owned.png'), findsNothing);
        expect(find.text('result row'), findsNothing);
        expect(find.text('Approve'), findsNothing);
      } else {
        expect(find.text('Open task'), findsOneWidget);
      }
      await tester.pumpWidget(const SizedBox());
      session.dispose();
      transport.close();
    });
  }
  testWidgets('实际文件读取403关闭详情和主草稿', (tester) async {
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') return json(user());
          if (request.url.path.endsWith('/runtime')) return json(runtime());
          expect(request.url.path,
              '/api/v1/workflow/todos/$id/form-attachments/$id/content');
          return json({
            'type': 'about:blank',
            'title': 'Forbidden',
            'status': 403,
            'code': 'workflow.todo.forbidden'
          }, 403);
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(session, WorkflowTodoClient(transport)));
    await tester.tap(find.text('Open task'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('source-owned.png'));
    await tester.pumpAndSettle();
    expect(find.text('source-owned.png'), findsNothing);
    expect(find.text('sensitive row'), findsNothing);
    expect(find.text('Approve'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
}
