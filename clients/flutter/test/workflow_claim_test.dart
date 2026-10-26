import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:fullnet_client/core/http/full_net_http_client.dart';
import 'package:fullnet_client/core/auth/identity_session.dart';
import 'package:fullnet_client/features/workflow/workflow_models.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_client.dart';
import 'package:fullnet_client/features/workflow/workflow_todo_detail_page.dart';
import 'package:fullnet_client/features/workflow/workflow_claim_pools_page.dart';
import 'package:fullnet_client/features/workflow/workflow_todos_page.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';

const id = '01912345-6789-7abc-8def-0123456789ab';
Map<String, dynamic> receipt({bool released = false}) => {
      'id': id,
      'definitionVersionId': id,
      'formVersionId': id,
      'businessType': 'claim',
      'businessId': 'one',
      'businessTitle': null,
      'statusKey': 'active',
      'revision': 2,
      'activeTodoId': released ? null : id,
      'startedAtUtc': '2026-10-25T00:00:00Z'
    };
Map<String, dynamic> claimDetail() => {
      'id': id,
      'instanceId': '01912345-6789-7abc-8def-0123456789ac',
      'stepId': '01912345-6789-7abc-8def-0123456789ad',
      'assigneeUserId': id,
      'statusKey': 'active',
      'revision': 1,
      'submissionRevision': 2,
      'submission': <String, dynamic>{},
      'activityKindKey': 'human.claim',
      'allowedActions': ['complete', 'release'],
      'signRequired': false,
      'commentRequired': false,
      'approvalModeKey': 'none',
      'requiredApprovalCount': 0,
      'approvedCount': 0,
      'rejectedCount': 0,
      'pendingCount': 0,
      'claimProgress': {
        'poolStatusKey': 'claimed',
        'poolRevision': 2,
        'claimCycle': 1,
        'allowRelease': true
      }
    };
http.Response jsonResponse(Object value) =>
    http.Response(jsonEncode(value), 200,
        headers: {'content-type': 'application/json'});
Map<String, dynamic> user(List<String> permissions) => {
      'id': id,
      'username': 'candidate',
      'displayName': 'Candidate',
      'preferredLocale': 'en-US',
      'profileVersion': 1,
      'permissions': permissions
    };
Widget app(Widget home) => MaterialApp(
    locale: const Locale('en', 'US'),
    localizationsDelegates: const [AppLocalizations.delegate],
    supportedLocales: AppLocalizations.supportedLocales,
    home: home);

void main() {
  test('claim runtime accepts the separate delegation action', () {
    expect(
        WorkflowTodoDetail.fromJson({
          ...claimDetail(),
          'allowedActions': ['complete', 'delegate']
        }).allowedActions,
        ['complete', 'delegate']);
  });
  test('claim guard requires owned progress and rejects approval facts', () {
    expect(WorkflowTodoDetail.fromJson(claimDetail()).claimProgress!.cycle, 1);
    expect(
        () => WorkflowTodoDetail.fromJson(
            {...claimDetail(), 'claimProgress': null}),
        throwsFormatException);
    expect(
        () =>
            WorkflowTodoDetail.fromJson({...claimDetail(), 'approvedCount': 1}),
        throwsFormatException);
    expect(
        () => WorkflowClaimProgress.fromJson({
              'poolStatusKey': 'claimed',
              'poolRevision': 2,
              'claimCycle': 32,
              'allowRelease': true
            }),
        throwsFormatException);
    expect(
        () => parseWorkflowClaimReceipt({...receipt(), 'activeTodoId': 'fake'},
            released: false),
        throwsFormatException);
  });
  testWidgets(
      'candidate read permission does not grant acquisition and the list initializes with localization',
      (tester) async {
    var queries = 0;
    final httpClient = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return jsonResponse(user(['workflow.claim_pools.read']));
          }
          queries++;
          return jsonResponse({
            'items': <Map<String, dynamic>>[],
            'totalCount': 0,
            'page': 1,
            'pageSize': 100
          });
        }));
    final session = IdentitySession(httpClient);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(WorkflowTodosPage(
        session: session,
        client: WorkflowTodoClient(httpClient),
        onSignedOut: () {})));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.tap(find.byTooltip('Available tasks'));
    await tester.pumpAndSettle();
    expect(queries, 1);
    expect(find.text('Claim task'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    httpClient.close();
  });
  testWidgets('unknown acquisition result retries the same exact body and key',
      (tester) async {
    final bodies = <Map<String, dynamic>>[];
    final httpClient = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          if (request.url.path == '/api/v1/me') {
            return jsonResponse(user(
                ['workflow.claim_pools.read', 'workflow.claim_pools.claim']));
          }
          if (request.method == 'POST') {
            bodies.add(jsonDecode(request.body) as Map<String, dynamic>);
            if (bodies.length == 1) {
              return http.Response(
                  jsonEncode({
                    'type': 'about:blank',
                    'status': 503,
                    'title': 'Unavailable',
                    'code': 'test.unavailable'
                  }),
                  503,
                  headers: {'content-type': 'application/problem+json'});
            }
            return jsonResponse(receipt());
          }
          return jsonResponse({
            'items': bodies.length < 2
                ? [
                    {
                      'id': id,
                      'instanceId': id,
                      'businessType': 'claim',
                      'businessId': 'one',
                      'businessTitle': 'Candidate task',
                      'revision': 1,
                      'claimCycle': 0,
                      'createdAtUtc': '2026-10-25T00:00:00Z',
                      'dueAtUtc': null
                    }
                  ]
                : <Map<String, dynamic>>[],
            'totalCount': bodies.length < 2 ? 1 : 0,
            'page': 1,
            'pageSize': 100
          });
        }));
    final session = IdentitySession(httpClient);
    await session.refreshCurrentUser();
    await tester.pumpWidget(app(WorkflowClaimPoolsPage(
        session: session, client: WorkflowTodoClient(httpClient))));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Claim task'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Claim task'));
    await tester.pumpAndSettle();
    expect(bodies.length, 2);
    expect(bodies[0], bodies[1]);
    expect(bodies[0].keys.toSet(), {'expectedRevision', 'idempotencyKey'});
    await tester.pumpWidget(const SizedBox());
    httpClient.close();
  });
  for (final allowed in [false, true]) {
    testWidgets('release requires its own permission: $allowed',
        (tester) async {
      Map<String, dynamic>? released;
      final httpClient = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') {
              return jsonResponse(user([
                'workflow.todos.read',
                'workflow.todos.complete',
                if (allowed) 'workflow.todos.release'
              ]));
            }
            if (request.url.path.endsWith('/runtime')) {
              return jsonResponse(claimDetail());
            }
            expect(request.url.path.endsWith('/release'), isTrue);
            released = jsonDecode(request.body) as Map<String, dynamic>;
            return jsonResponse(receipt(released: true));
          }));
      final session = IdentitySession(httpClient);
      await session.refreshCurrentUser();
      await tester.pumpWidget(app(Builder(
          builder: (context) => Scaffold(
              body: TextButton(
                  onPressed: () => Navigator.of(context).push(
                      MaterialPageRoute<void>(
                          builder: (_) => WorkflowTodoDetailPage(
                              todoId: id,
                              session: session,
                              client: WorkflowTodoClient(httpClient)))),
                  child: const Text('Open'))))));
      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();
      expect(
          find.text('Release task'), allowed ? findsOneWidget : findsNothing);
      if (allowed) {
        await tester.tap(find.text('Release task'));
        await tester.pumpAndSettle();
        expect(released!['expectedPoolRevision'], 2);
        expect(released!['expectedFormRevision'], 2);
        expect(released!.containsKey('fieldPatch'), isFalse);
      } else {
        expect(released, isNull);
      }
      await tester.pumpWidget(const SizedBox());
      httpClient.close();
    });
  }
}
