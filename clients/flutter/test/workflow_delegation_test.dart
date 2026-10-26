import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/features/workflow/workflow_delegation_models.dart';
import 'package:fullnet_client/features/workflow/workflow_delegation_panel.dart';
import 'package:fullnet_client/features/workflow/workflow_models.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_detail_page.dart';
import 'package:fullnet_client/features/workflow/workflow_todos_page.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';

const origin = '01912345-6789-7abc-8def-0123456789a1';
const proxy = '01912345-6789-7abc-8def-0123456789a2';
const actor = '01912345-6789-7abc-8def-0123456789a3';
const targetA = '01912345-6789-7abc-8def-0123456789a4';
const targetB = '01912345-6789-7abc-8def-0123456789a5';
http.Response json(Object body, [int status = 200]) =>
    http.Response(jsonEncode(body), status,
        headers: {'content-type': 'application/json'});
Map<String, dynamic> user(List<String> permissions) => {
      'id': actor,
      'username': 'owner',
      'displayName': 'Owner',
      'preferredLocale': 'en-US',
      'profileVersion': 1,
      'permissions': permissions
    };
Map<String, dynamic> detail({bool history = false}) => {
      'id': origin,
      'instanceId': origin,
      'stepId': proxy,
      'assigneeUserId': actor,
      'statusKey': history ? 'completed' : 'active',
      'revision': 3,
      'submissionRevision': 4,
      'submission': <String, dynamic>{},
      'allowedActions': ['approve', 'delegate'],
      'activityKindKey': 'human.approval',
      'signRequired': history,
      'commentRequired': history
    };
Map<String, dynamic> grant() => {
      'originTodoId': origin,
      'proxyTodoId': proxy,
      'originalAssigneeUserId': actor,
      'proxyUserId': targetA,
      'statusKey': 'active',
      'proxyRevision': 7,
      'formRevision': 9,
      'canRecall': true,
      'createdAtUtc': '2026-10-25T00:00:00Z'
    };
Map<String, dynamic> receipt() => {
      'id': origin,
      'definitionVersionId': actor,
      'formVersionId': actor,
      'businessType': 'delegation',
      'businessId': 'one',
      'businessTitle': null,
      'statusKey': 'active',
      'revision': 8,
      'activeTodoId': proxy,
      'startedAtUtc': '2026-10-25T00:00:00Z'
    };
Map<String, dynamic> candidates(int page) => {
      'page': page,
      'pageSize': 20,
      'total': 21,
      'items': page == 2
          ? [
              {'id': targetB, 'username': 'b', 'displayName': 'Proxy B'}
            ]
          : [
              {'id': actor, 'username': 'self', 'displayName': 'Self'},
              {'id': targetA, 'username': 'a', 'displayName': 'Proxy A'},
              ...List.generate(
                  18,
                  (i) => {
                        'id':
                            '01912345-6789-7abc-8def-01234567${(0xb0 + i).toRadixString(16).padLeft(4, '0')}',
                        'username': 'member$i',
                        'displayName': 'Member $i'
                      })
            ]
    };
Widget app(Widget home) => MaterialApp(
    locale: const Locale('en', 'US'),
    localizationsDelegates: const [AppLocalizations.delegate],
    supportedLocales: AppLocalizations.supportedLocales,
    home: home);
Widget panel(IdentitySession session, FullNetHttpClient transport,
        {bool history = false, VoidCallback? completed}) =>
    app(Scaffold(
        body: SingleChildScrollView(
            child: WorkflowDelegationPanel(
                detail: WorkflowTodoDetail.fromJson(detail(history: history)),
                client: WorkflowTodoClient(transport),
                session: session,
                comment: '',
                busy: false,
                unsaved: false,
                onBusy: (_) {},
                onCompleted: completed ?? () {},
                onRefresh: () async {}))));

void main() {
  test('delegation guard rejects impossible authority and validates real pages',
      () {
    expect(WorkflowTodoDelegation.parse({'delegation': null}), isNull);
    expect(WorkflowTodoDelegation.parse({'delegation': grant()})!.proxyRevision,
        7);
    for (final patch in [
      {'statusKey': 'completed'},
      {'formRevision': 0},
      {'proxyRevision': 0},
      {'proxyTodoId': origin},
      {'proxyUserId': actor},
      {'statusKey': 'admin'}
    ]) {
      expect(
          () => WorkflowTodoDelegation.parse({
                'delegation': {...grant(), ...patch}
              }),
          throwsFormatException);
    }
    expect(WorkflowDelegateCandidatePage.parse(candidates(1)).items.length, 20);
    expect(WorkflowDelegateCandidatePage.parse(candidates(2)).page, 2);
    expect(
        () => WorkflowDelegateCandidatePage.parse(
            {...candidates(1), 'pageSize': 100}),
        throwsFormatException);
  });

  test('mobile client sends distinct bounded delegation and recall bodies',
      () async {
    final requests = <http.Request>[];
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          requests.add(request);
          return json(receipt());
        }));
    final client = WorkflowTodoClient(transport);
    await client.delegate(origin, targetA,
        revision: 3, formRevision: 4, key: 'delegate', comment: 'reason');
    await client.recallDelegation(origin,
        revision: 7, formRevision: 9, key: 'recall', comment: 'recall reason');
    expect(requests[0].url.path, '/api/v1/workflow/todos/$origin/delegate');
    expect(jsonDecode(requests[0].body), {
      'assigneeUserId': targetA,
      'expectedRevision': 3,
      'expectedFormRevision': 4,
      'idempotencyKey': 'delegate',
      'comment': 'reason'
    });
    expect(requests[1].url.path,
        '/api/v1/workflow/todos/$origin/delegation/recall');
    expect(jsonDecode(requests[1].body), {
      'expectedRevision': 7,
      'expectedFormRevision': 9,
      'idempotencyKey': 'recall',
      'comment': 'recall reason'
    });
    await expectLater(
        client.listDelegationCandidates(origin, page: 0), throwsArgumentError);
    expect(requests.length, 2);
    transport.close();
  });

  for (final allowed in [false, true]) {
    testWidgets(
        'detail creates delegation controls only with their exact permission: $allowed',
        (tester) async {
      var candidateReads = 0;
      final transport = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') {
              return json(user([
                'workflow.todos.read',
                'workflow.todos.transfer',
                if (allowed) 'workflow.todos.delegate'
              ]));
            }
            if (request.url.path.endsWith('/runtime')) return json(detail());
            if (request.url.path.endsWith('/delegation-candidates')) {
              candidateReads++;
              return json(candidates(1));
            }
            return json({'delegation': null});
          }));
      final session = IdentitySession(transport);
      await session.refreshCurrentUser();
      await tester.pumpWidget(app(WorkflowTodoDetailPage(
          todoId: origin,
          session: session,
          client: WorkflowTodoClient(transport))));
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('workflow-delegate')),
          allowed ? findsOneWidget : findsNothing);
      expect(candidateReads, allowed ? 1 : 0);
      await tester.pumpWidget(const SizedBox());
      session.dispose();
      transport.close();
    });
  }

  testWidgets(
      'candidate paging preserves the selected proxy and a transient retry preserves its key',
      (tester) async {
    final writes = <Map<String, dynamic>>[];
    final pages = <String?>[];
    var completed = 0;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return json(
                user(['workflow.todos.read', 'workflow.todos.delegate']));
          }
          if (request.url.path.endsWith('/delegation')) {
            return json({'delegation': null});
          }
          if (request.url.path.endsWith('/delegation-candidates')) {
            pages.add(request.url.queryParameters['page']);
            return json(candidates(int.parse(pages.last!)));
          }
          writes.add(jsonDecode(request.body) as Map<String, dynamic>);
          if (writes.length == 1) {
            return json({
              'type': 'https://full.net/errors/temporarily_unavailable',
              'title': 'Unavailable',
              'status': 503,
              'code': 'temporarily_unavailable'
            }, 503);
          }
          return json(receipt());
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester
        .pumpWidget(panel(session, transport, completed: () => completed++));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    expect(find.text('Self'), findsNothing);
    await tester.tap(find.text('Proxy A').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(pages, ['1', '2']);
    expect(find.text('Proxy A'), findsOneWidget);
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    expect(find.text('Proxy B'), findsOneWidget);
    await tester.tap(find.text('Proxy A').last);
    await tester.pumpAndSettle();
    final submit = find.byKey(const ValueKey('workflow-delegate'));
    await tester.ensureVisible(submit);
    await tester.tap(submit);
    await tester.pumpAndSettle();
    await tester.ensureVisible(submit);
    await tester.tap(submit);
    await tester.pumpAndSettle();
    expect(writes.length, 2);
    expect(writes[0]['assigneeUserId'], targetA);
    expect(writes[0]['idempotencyKey'], writes[1]['idempotencyKey']);
    expect(writes[0].containsKey('fieldPatch'), isFalse);
    expect(writes[0].containsKey('tenantId'), isFalse);
    expect(completed, 1);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });

  for (final allowed in [false, true]) {
    testWidgets(
        'historical recall uses separate permission and latest revisions without a proxy signature: $allowed',
        (tester) async {
      final writes = <Map<String, dynamic>>[];
      var candidatesRead = 0;
      var completed = 0;
      final transport = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') {
              return json(user([
                'workflow.todos.read',
                if (allowed) 'workflow.todos.recall_delegation'
              ]));
            }
            if (request.url.path.endsWith('/delegation-candidates')) {
              candidatesRead++;
              return json(candidates(1));
            }
            if (request.method == 'GET') return json({'delegation': grant()});
            expect(request.url.path,
                '/api/v1/workflow/todos/$origin/delegation/recall');
            writes.add(jsonDecode(request.body) as Map<String, dynamic>);
            return json(receipt());
          }));
      final session = IdentitySession(transport);
      await session.refreshCurrentUser();
      await tester.pumpWidget(panel(session, transport,
          history: true, completed: () => completed++));
      await tester.pumpAndSettle();
      final submit = find.byKey(const ValueKey('workflow-recall-delegation'));
      expect(submit, allowed ? findsOneWidget : findsNothing);
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      if (allowed) {
        expect(tester.widget<OutlinedButton>(submit).onPressed, isNull);
        await tester.enterText(find.byType(TextField), 'Recall reason');
        tester.testTextInput.hide();
        await tester.pumpAndSettle();
        await tester.ensureVisible(submit);
        await tester.tap(submit);
        await tester.pumpAndSettle();
        expect(writes.single['expectedRevision'], 7);
        expect(writes.single['expectedFormRevision'], 9);
        expect(writes.single['comment'], 'Recall reason');
        expect(writes.single.containsKey('signatureFileId'), isFalse);
        expect(completed, 1);
      } else {
        expect(writes, isEmpty);
      }
      expect(candidatesRead, 0);
      await tester.pumpWidget(const SizedBox());
      session.dispose();
      transport.close();
    });
  }

  testWidgets('delegated task history loads a real second page',
      (tester) async {
    final pages = <String?>[];
    Map<String, dynamic> row(int i) => {
          'id':
              '01912345-6789-7abc-8def-01234567${(0xc0 + i).toRadixString(16).padLeft(4, '0')}',
          'instanceId': origin,
          'stepId': proxy,
          'statusKey': 'completed',
          'revision': 2,
          'arrivedAtUtc': '2026-10-25T00:00:00Z',
          'completedAtUtc': '2026-10-25T00:01:00Z',
          'resultActionKey': 'delegate',
          'businessType': 'delegation',
          'businessId': '$i',
          'businessTitle': null,
          'instanceStatusKey': 'active',
          'definitionKey': 'delegated-$i',
          'nodeKey': 'work'
        };
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return json(user(['workflow.todos.read']));
          }
          if (request.url.path.endsWith('/history')) {
            expect(request.url.queryParameters['resultActionKey'], 'delegate');
            pages.add(request.url.queryParameters['page']);
            final page = int.parse(pages.last!);
            return json({
              'items':
                  page == 1 ? List.generate(20, (i) => row(i + 1)) : [row(21)],
              'page': page,
              'pageSize': 20,
              'total': 21
            });
          }
          return json({
            'items': <Map<String, dynamic>>[],
            'page': 1,
            'pageSize': 20,
            'total': 0
          });
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(WorkflowTodosPage(
        session: session,
        client: WorkflowTodoClient(transport),
        onSignedOut: () {})));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('workflow-delegated-tab')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Next'));
    await tester.pumpAndSettle();
    expect(pages, ['1', '2']);
    expect(find.text('delegated-21'), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
}
