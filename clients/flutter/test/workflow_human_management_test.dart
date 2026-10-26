import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/features/workflow/workflow_models.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';
import 'package:fullnet_client/features/workflow/workflow_human_management_models.dart';
import 'package:fullnet_client/features/workflow/workflow_human_management_panel.dart';
import 'workflow_delegation_test.dart' as fixtures;

const origin = fixtures.origin;
const first = fixtures.targetA;
const second = fixtures.targetB;
Map<String, dynamic> detail(String action, {bool history = false}) => {
      ...fixtures.detail(history: history),
      'allowedActions': [action],
      'signRequired': history,
      'commentRequired': history
    };
Widget panel(IdentitySession session, FullNetHttpClient http, String action,
        {bool history = false,
        VoidCallback? completed,
        Future<void> Function()? refresh}) =>
    fixtures.app(Scaffold(
        body: SingleChildScrollView(
            child: WorkflowHumanManagementPanel(
                detail: WorkflowTodoDetail.fromJson(
                    detail(action, history: history)),
                client: WorkflowTodoClient(http),
                session: session,
                comment: action == 'return' ? 'Return reason' : '',
                busy: false,
                unsaved: false,
                onBusy: (_) {},
                onCompleted: completed ?? () {},
                onRefresh: refresh ?? () async {}))));
Future<void> choose(WidgetTester tester, String label) async {
  await tester.tap(find.byType(DropdownButtonFormField<String>));
  await tester.pumpAndSettle();
  await tester.tap(find.text(label).last);
  await tester.pumpAndSettle();
}

Future<void> click(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Map<String, dynamic> chain() => {
      'chainId': second,
      'directionKey': 'after',
      'statusKey': 'active',
      'items': [
        {
          'itemId': first,
          'sequenceNo': 1,
          'assigneeUserId': second,
          'statusKey': 'active',
          'todoId': origin
        }
      ]
    };

void main() {
  test(
      'management guards preserve multi-person targets and reject forged states',
      () {
    final targets = [
      {
        'stepId': first,
        'nodeKey': 'group',
        'assigneeUserId': null,
        'completedAtUtc': '2026-10-26T00:00:00Z'
      }
    ];
    expect(WorkflowReturnTarget.parsePage(targets).single.stepId, first);
    expect(() => WorkflowReturnTarget.parsePage([...targets, ...targets]),
        throwsFormatException);
    expect(WorkflowCountersignChain.parse(chain()).items.single.userId, second);
    expect(
        () => WorkflowCountersignChain.parse(
            {...chain(), 'statusKey': 'unknown'}),
        throwsFormatException);
    expect(
        () => validateWorkflowManagementReceipt(
            {...fixtures.receipt(), 'statusKey': 'completed'}),
        throwsFormatException);
    validateWorkflowManagementReceipt(
        {...fixtures.receipt(), 'activeTodoId': null});
  });
  test(
      'client management bodies are bounded and do not invent actors or form patches',
      () async {
    final calls = <http.Request>[];
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          calls.add(request);
          return fixtures.json({...fixtures.receipt(), 'activeTodoId': null});
        }));
    final client = WorkflowTodoClient(transport);
    await client.transfer(origin, first,
        revision: 3, formRevision: 4, key: 'transfer', comment: 'Transfer');
    await client.returnTodo(origin, first,
        revision: 3, formRevision: 4, key: 'return', comment: 'Return');
    await client.countersign(origin, 'before', [first, second],
        revision: 3, key: 'counter');
    await client.cancelCountersign(origin, revision: 3, key: 'cancel');
    expect(jsonDecode(calls[0].body), {
      'assigneeUserId': first,
      'expectedRevision': 3,
      'expectedFormRevision': 4,
      'idempotencyKey': 'transfer',
      'comment': 'Transfer'
    });
    expect(jsonDecode(calls[1].body), {
      'targetStepId': first,
      'expectedRevision': 3,
      'expectedFormRevision': 4,
      'fieldPatch': <String, dynamic>{},
      'idempotencyKey': 'return',
      'comment': 'Return'
    });
    expect(jsonDecode(calls[2].body), {
      'directionKey': 'before',
      'assigneeUserIds': [first, second],
      'expectedRevision': 3,
      'idempotencyKey': 'counter'
    });
    expect(jsonDecode(calls[3].body),
        {'expectedRevision': 3, 'idempotencyKey': 'cancel'});
    await expectLater(
        client.countersign(origin, 'before', [first, first],
            revision: 3, key: 'duplicate'),
        throwsArgumentError);
    expect(calls.length, 4);
    transport.close();
  });
  for (final allowed in [false, true]) {
    testWidgets('transfer uses its exact permission: $allowed', (tester) async {
      var reads = 0;
      final transport = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') {
              return fixtures.json(fixtures.user([
                'workflow.todos.read',
                if (allowed) 'workflow.todos.transfer'
              ]));
            }
            reads++;
            return fixtures.json(fixtures.candidates(1));
          }));
      final session = IdentitySession(transport);
      await session.refreshCurrentUser();
      await tester.pumpWidget(panel(session, transport, 'transfer'));
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('workflow-management-transfer')),
          allowed ? findsOneWidget : findsNothing);
      expect(reads, allowed ? 1 : 0);
      await tester.pumpWidget(const SizedBox());
      session.dispose();
      transport.close();
    });
  }
  testWidgets(
      'transfer preserves an off-page target and reuses its key after transient failure',
      (tester) async {
    final writes = <Map<String, dynamic>>[];
    final pages = <String?>[];
    var completed = 0;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return fixtures.json(fixtures
                .user(['workflow.todos.read', 'workflow.todos.transfer']));
          }
          if (request.url.path.endsWith('/transfer-candidates')) {
            pages.add(request.url.queryParameters['page']);
            return fixtures.json(fixtures.candidates(int.parse(pages.last!)));
          }
          writes.add(jsonDecode(request.body) as Map<String, dynamic>);
          if (writes.length == 1) {
            return fixtures.json({
              'type': 'https://full.net/errors/unavailable',
              'title': 'Unavailable',
              'status': 503,
              'code': 'unavailable'
            }, 503);
          }
          return fixtures.json(fixtures.receipt());
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(
        panel(session, transport, 'transfer', completed: () => completed++));
    await tester.pumpAndSettle();
    expect(find.text('Self'), findsNothing);
    await choose(tester, 'Proxy A');
    await click(tester, find.text('Next'));
    expect(pages, ['1', '2']);
    expect(find.text('Proxy A'), findsOneWidget);
    await click(
        tester, find.byKey(const ValueKey('workflow-management-submit')));
    await click(
        tester, find.byKey(const ValueKey('workflow-management-submit')));
    expect(writes.length, 2);
    expect(writes[0]['assigneeUserId'], first);
    expect(writes[0]['idempotencyKey'], writes[1]['idempotencyKey']);
    expect(completed, 1);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
  testWidgets(
      'countersign preserves member order across pages without a business signature',
      (tester) async {
    Map<String, dynamic>? write;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return fixtures.json(fixtures
                .user(['workflow.todos.read', 'workflow.todos.countersign']));
          }
          if (request.url.path.endsWith('/countersign-chain')) {
            return fixtures.json({
              'type':
                  'https://full.net/errors/workflow.todo.countersign_chain_not_found',
              'title': 'No chain',
              'status': 404,
              'code': 'workflow.todo.countersign_chain_not_found'
            }, 404);
          }
          if (request.url.path.endsWith('/countersign-candidates')) {
            return fixtures.json(fixtures
                .candidates(int.parse(request.url.queryParameters['page']!)));
          }
          write = jsonDecode(request.body) as Map<String, dynamic>;
          return fixtures.json({...fixtures.receipt(), 'activeTodoId': null});
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(panel(session, transport, 'countersign'));
    await tester.pumpAndSettle();
    await choose(tester, 'Proxy A');
    await click(tester, find.text('Add in order'));
    await click(tester, find.text('Next'));
    await choose(tester, 'Proxy B');
    await click(tester, find.text('Add in order'));
    await click(tester, find.text('After'));
    await click(
        tester, find.byKey(const ValueKey('workflow-management-submit')));
    expect(write, {
      'directionKey': 'after',
      'assigneeUserIds': [first, second],
      'expectedRevision': 3,
      'idempotencyKey': isA<String>(),
      'comment': ''
    });
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
  testWidgets(
      'historical after-countersign cancellation has an independent reason and no proxy signature',
      (tester) async {
    Map<String, dynamic>? write;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return fixtures.json(fixtures
                .user(['workflow.todos.read', 'workflow.todos.countersign']));
          }
          if (request.url.path.endsWith('/countersign-chain')) {
            return fixtures.json(chain());
          }
          write = jsonDecode(request.body) as Map<String, dynamic>;
          return fixtures.json({
            ...fixtures.receipt(),
            'statusKey': 'completed',
            'activeTodoId': null
          });
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester
        .pumpWidget(panel(session, transport, 'countersign', history: true));
    await tester.pumpAndSettle();
    expect(find.byType(DropdownButtonFormField<String>), findsNothing);
    await tester.enterText(find.byType(TextField), 'Cancel consultation');
    await tester.pumpAndSettle();
    expect(
        tester
            .widget<OutlinedButton>(
                find.byKey(const ValueKey('workflow-management-cancel')))
            .onPressed,
        isNotNull);
    await click(
        tester, find.byKey(const ValueKey('workflow-management-cancel')));
    expect(write, {
      'expectedRevision': 3,
      'idempotencyKey': isA<String>(),
      'comment': 'Cancel consultation'
    });
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
  testWidgets(
      'return selects a multi-person historical step and submits its two revisions',
      (tester) async {
    Map<String, dynamic>? write;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return fixtures.json(fixtures
                .user(['workflow.todos.read', 'workflow.todos.return']));
          }
          if (request.url.path.endsWith('/return-targets')) {
            return fixtures.json([
              {
                'stepId': first,
                'nodeKey': 'group',
                'assigneeUserId': null,
                'completedAtUtc': '2026-10-26T00:00:00Z'
              }
            ]);
          }
          write = jsonDecode(request.body) as Map<String, dynamic>;
          return fixtures.json({...fixtures.receipt(), 'activeTodoId': null});
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester.pumpWidget(panel(session, transport, 'return'));
    await tester.pumpAndSettle();
    await choose(tester, 'group · 2026-10-26T00:00:00Z');
    await click(
        tester, find.byKey(const ValueKey('workflow-management-submit')));
    expect(write, {
      'targetStepId': first,
      'expectedRevision': 3,
      'expectedFormRevision': 4,
      'fieldPatch': <String, dynamic>{},
      'comment': 'Return reason',
      'idempotencyKey': isA<String>()
    });
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
  testWidgets('reminder submits actual instance and step revisions',
      (tester) async {
    Map<String, dynamic>? write;
    var refreshed = 0;
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return fixtures.json(fixtures
                .user(['workflow.todos.read', 'workflow.instances.remind']));
          }
          if (request.url.path.endsWith('/reminder-targets')) {
            return fixtures.json({
              'instanceRevision': 7,
              'items': [
                {
                  'stepId': fixtures.proxy,
                  'nodeKey': 'review',
                  'nodeTypeKey': 'human.approval',
                  'stepRevision': 8,
                  'activeTodoCount': 1,
                  'manualCount': 0,
                  'lastRequestedAtUtc': null,
                  'canRemind': true
                }
              ]
            });
          }
          write = jsonDecode(request.body) as Map<String, dynamic>;
          return fixtures.json({
            'requestId': second,
            'stepId': fixtures.proxy,
            'instanceRevision': 7,
            'stepRevision': 8,
            'manualSequence': 1,
            'recipientCount': 1,
            'acceptedAtUtc': '2026-10-26T00:00:00Z'
          });
        }));
    final session = IdentitySession(transport);
    await session.refreshCurrentUser();
    await tester
        .pumpWidget(panel(session, transport, 'remind', refresh: () async {
      refreshed++;
    }));
    await tester.pumpAndSettle();
    await click(
        tester, find.byKey(const ValueKey('workflow-management-remind')));
    expect(write, {
      'stepId': fixtures.proxy,
      'expectedInstanceRevision': 7,
      'expectedStepRevision': 8,
      'idempotencyKey': isA<String>()
    });
    expect(refreshed, 1);
    await tester.pumpWidget(const SizedBox());
    session.dispose();
    transport.close();
  });
}
