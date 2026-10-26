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
import 'package:fullnet_client/l10n/app_localizations.dart';

const todoId = '01912345-6789-7abc-8def-0123456789ab';
Map<String, dynamic> task() => {
      'id': todoId,
      'instanceId': '01912345-6789-7abc-8def-0123456789ac',
      'stepId': '01912345-6789-7abc-8def-0123456789ad',
      'assigneeUserId': todoId,
      'statusKey': 'active',
      'revision': 1,
      'submissionRevision': 2,
      'submission': <String, dynamic>{},
      'activityKindKey': 'human.task',
      'allowedActions': ['complete'],
      'signRequired': false,
      'commentRequired': false,
      'approvalModeKey': 'none',
      'requiredApprovalCount': 0,
      'approvedCount': 0,
      'rejectedCount': 0,
      'pendingCount': 0,
      'taskProgress': {
        'completionModeKey': 'all',
        'memberCount': 3,
        'requiredCompletionCount': 3,
        'completedCount': 1,
        'pendingCount': 2,
        'cancelledCount': 0
      }
    };

void main() {
  test('task runtime accepts the separate delegation action', () {
    expect(
        WorkflowTodoDetail.fromJson({
          ...task(),
          'allowedActions': ['complete', 'delegate']
        }).allowedActions,
        ['complete', 'delegate']);
  });
  for (final allowed in [false, true]) {
    testWidgets(
        'task button requires complete permission, not approve: $allowed',
        (tester) async {
      var completed = 0;
      final httpClient = FullNetHttpClient(
          apiBaseUrl: 'https://api.example.test',
          inner: MockClient((request) async {
            if (request.url.path == '/api/v1/me') {
              return http.Response(
                  jsonEncode({
                    'id': todoId,
                    'username': 'member',
                    'displayName': 'Member',
                    'preferredLocale': 'en-US',
                    'profileVersion': 1,
                    'permissions': [
                      'workflow.todos.read',
                      'workflow.todos.approve',
                      if (allowed) 'workflow.todos.complete'
                    ]
                  }),
                  200);
            }
            if (request.url.path.endsWith('/runtime')) {
              return http.Response(jsonEncode(task()), 200);
            }
            expect(request.url.path.endsWith('/complete'), isTrue);
            completed++;
            return http.Response('{}', 200);
          }));
      final session = IdentitySession(httpClient);
      await session.refreshCurrentUser();
      await tester.pumpWidget(MaterialApp(
          locale: const Locale('en', 'US'),
          localizationsDelegates: const [AppLocalizations.delegate],
          supportedLocales: AppLocalizations.supportedLocales,
          home: Builder(
              builder: (context) => Scaffold(
                  body: TextButton(
                      onPressed: () => Navigator.of(context).push(
                          MaterialPageRoute<void>(
                              builder: (_) => WorkflowTodoDetailPage(
                                  todoId: todoId,
                                  session: session,
                                  client: WorkflowTodoClient(httpClient)))),
                      child: const Text('Open task'))))));
      await tester.tap(find.text('Open task'));
      await tester.pumpAndSettle();
      expect(find.text('Approve'), findsNothing);
      expect(find.text('Completed 1/3, pending 2'), findsOneWidget);
      expect(
          find.text('Complete task'), allowed ? findsOneWidget : findsNothing);
      if (allowed) {
        await tester.tap(find.text('Complete task'));
        await tester.pumpAndSettle();
        expect(completed, 1);
        expect(find.text('Open task'), findsOneWidget);
      } else {
        expect(completed, 0);
      }
      await tester.pumpWidget(const SizedBox());
      httpClient.close();
    });
  }
  test('task progress remains separate from approval votes', () {
    final detail = WorkflowTodoDetail.fromJson(task());
    expect(detail.activityKindKey, 'human.task');
    expect(detail.taskProgress!.memberCount, 3);
    expect(detail.taskProgress!.completedCount, 1);
    expect(detail.allowedActions, ['complete']);
  });
  test('task rejects approval actions and forged thresholds', () {
    for (final patch in [
      {
        'allowedActions': ['approve']
      },
      {'requiredApprovalCount': 1},
      {'taskProgress': null},
      {
        'taskProgress': {
          ...task()['taskProgress'] as Map<String, dynamic>,
          'requiredCompletionCount': 2
        }
      },
      {
        'taskProgress': {
          ...task()['taskProgress'] as Map<String, dynamic>,
          'pendingCount': 3
        }
      },
      {'activityKindKey': 'human.approval'},
    ]) {
      expect(() => WorkflowTodoDetail.fromJson({...task(), ...patch}),
          throwsFormatException);
    }
  });
  test('complete posts the owned action with both revisions and evidence',
      () async {
    var calls = 0;
    final httpClient = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        accessToken: 'current',
        inner: MockClient((request) async {
          calls++;
          expect(request.url.path, '/api/v1/workflow/todos/$todoId/complete');
          expect(request.headers['Authorization'], 'Bearer current');
          expect(jsonDecode(request.body), {
            'expectedRevision': 1,
            'expectedFormRevision': 2,
            'fieldPatch': {'title': 'ready'},
            'comment': 'done',
            'signatureFileId': todoId,
            'idempotencyKey': 'complete-once'
          });
          return http.Response('{}', 200);
        }));
    await WorkflowTodoClient(httpClient).complete(
        todoId,
        const ActWorkflowTodoRequest(
            expectedRevision: 1,
            expectedFormRevision: 2,
            fieldPatch: {'title': 'ready'},
            comment: 'done',
            signatureFileId: todoId,
            idempotencyKey: 'complete-once'));
    expect(calls, 1);
    httpClient.close();
  });
}
