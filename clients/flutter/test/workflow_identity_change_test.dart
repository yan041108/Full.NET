import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_detail_page.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';

const id = '01912345-6789-7abc-8def-0123456789a1';
http.Response json(Object body) => http.Response(jsonEncode(body), 200,
    headers: {'content-type': 'application/json'});
Map<String, dynamic> user(String name) => {
      'id': id,
      'username': name,
      'displayName': name,
      'preferredLocale': 'en-US',
      'profileVersion': 1,
      'permissions': ['workflow.todos.read', 'workflow.todos.approve']
    };
Map<String, dynamic> detail() => {
      'id': id,
      'instanceId': '01912345-6789-7abc-8def-0123456789ac',
      'stepId': '01912345-6789-7abc-8def-0123456789ad',
      'assigneeUserId': id,
      'statusKey': 'active',
      'revision': 1,
      'submissionRevision': 1,
      'submission': {'document': 'old-identity-secret'},
      'allowedActions': ['approve'],
      'activityKindKey': 'human.approval',
      'signRequired': false,
      'commentRequired': false
    };
Widget app(Widget home) => MaterialApp(
    locale: const Locale('en', 'US'),
    localizationsDelegates: const [AppLocalizations.delegate],
    supportedLocales: AppLocalizations.supportedLocales,
    home: home);

void main() {
  test(
      'logout notifies consumers and a late profile cannot restore the previous identity',
      () async {
    final response = Completer<http.Response>();
    final requested = Completer<void>();
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) {
          requested.complete();
          return response.future;
        }));
    final session = IdentitySession(transport);
    var changes = 0;
    session.addListener(() => changes++);
    final refresh = session.refreshCurrentUser();
    await requested.future;
    session.logout();
    response.complete(json(user('old')));
    await refresh;
    expect(session.currentUser, isNull);
    expect(changes, 1);
    session.dispose();
    transport.close();
  });

  test('an older login response cannot replace the newer token or identity',
      () async {
    final firstReply = Completer<http.Response>();
    final firstRequested = Completer<void>();
    String? bearer;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path.endsWith('/login')) {
            if ((jsonDecode(request.body)
                    as Map<String, dynamic>)['username'] ==
                'first') {
              firstRequested.complete();
              return firstReply.future;
            }
            return json({'accessToken': 'second-token'});
          }
          bearer = request.headers['authorization'];
          return json(user('second'));
        }));
    final session = IdentitySession(transport);
    final first = session.login(username: 'first', password: 'password');
    await firstRequested.future;
    await session.login(username: 'second', password: 'password');
    firstReply.complete(json({'accessToken': 'first-token'}));
    await first;
    await session.refreshCurrentUser();
    expect(session.currentUser!.username, 'second');
    expect(bearer, 'Bearer second-token');
    session.dispose();
    transport.close();
  });

  testWidgets(
      'logout during runtime loading removes old data and rejects its late result',
      (tester) async {
    final response = Completer<http.Response>();
    final requested = Completer<void>();
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) {
          if (request.url.path == '/api/v1/me') {
            return Future.value(json(user('old')));
          }
          requested.complete();
          return response.future;
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(WorkflowTodoDetailPage(
        todoId: id, session: session, client: WorkflowTodoClient(transport))));
    await tester.pump();
    expect(requested.isCompleted, isTrue);
    session.logout();
    await tester.pump();
    response.complete(json(detail()));
    await tester.pumpAndSettle();
    expect(find.textContaining('old-identity-secret'), findsNothing);
    expect(find.text('Approve'), findsNothing);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });

  testWidgets('a late old-identity decision cannot pop the page after logout',
      (tester) async {
    final reply = Completer<http.Response>();
    final sent = Completer<void>();
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) {
          if (request.url.path == '/api/v1/me') {
            return Future.value(json(user('old')));
          }
          if (request.url.path.endsWith('/runtime')) {
            return Future.value(json(detail()));
          }
          sent.complete();
          return reply.future;
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(Builder(
        builder: (context) => Scaffold(
            body: TextButton(
                onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                        builder: (_) => WorkflowTodoDetailPage(
                            todoId: id,
                            session: session,
                            client: WorkflowTodoClient(transport)))),
                child: const Text('Open'))))));
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Approve'));
    await tester.tap(find.text('Approve'));
    await tester.pump();
    expect(sent.isCompleted, isTrue);
    session.logout();
    await tester.pump();
    reply.complete(json({}));
    await tester.pumpAndSettle();
    expect(find.byType(WorkflowTodoDetailPage), findsOneWidget);
    expect(find.textContaining('old-identity-secret'), findsNothing);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
}
