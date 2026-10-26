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
Map<String, dynamic> vote({String kind = 'text'}) => {
      'id': todoId,
      'instanceId': '01912345-6789-7abc-8def-0123456789ac',
      'stepId': '01912345-6789-7abc-8def-0123456789ad',
      'assigneeUserId': todoId,
      'statusKey': 'active',
      'revision': 1,
      'submissionRevision': 4,
      'submission': <String, dynamic>{},
      'activityKindKey': 'human.vote',
      'allowedActions': ['vote'],
      'signRequired': false,
      'commentRequired': false,
      'approvalModeKey': 'none',
      'requiredApprovalCount': 0,
      'approvedCount': 0,
      'rejectedCount': 0,
      'pendingCount': 0,
      'voteForm': {
        'formVersionId': todoId,
        'formSchemaHash': 'a' * 64,
        'formSchema': {
          'schemaVersion': 1,
          'adapterVersion': 1,
          'sections': [
            {
              'sectionKey': 'ballot',
              'fields': [
                {
                  'fieldKey': 'answer',
                  'fieldTypeKey': kind,
                  'required': true,
                  'constraints':
                      kind == 'decimal' ? {'scale': 8} : <String, dynamic>{}
                }
              ]
            }
          ]
        },
        'values': <String, dynamic>{},
        'fieldPolicies': {'answer': 'required'},
        'revision': 2,
        'choiceKey': null,
        'readOnly': false,
        'allowAbstain': true,
      },
      'voteProgress': {
        'memberCount': 3,
        'requiredParticipationCount': 2,
        'submittedCount': 0,
        'pendingCount': 3,
        'cancelledCount': 0,
        'statusKey': 'active',
        'closesAtUtc': null,
        'yesCount': null,
        'noCount': null,
        'abstainCount': null,
        'unsubmittedCount': null
      },
    };

void main() {
  test('private vote runtime accepts the separate delegation action', () {
    expect(
        WorkflowTodoDetail.fromJson({
          ...vote(),
          'allowedActions': ['vote', 'delegate']
        }).allowedActions,
        ['vote', 'delegate']);
  });
  for (final allowed in [false, true]) {
    testWidgets(
        'private vote requires its own permission and preserves retry identity: $allowed',
        (tester) async {
      final bodies = <Map<String, dynamic>>[];
      final transport = FullNetHttpClient(
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
                      if (allowed) 'workflow.todos.vote'
                    ]
                  }),
                  200);
            }
            if (request.url.path.endsWith('/runtime')) {
              return http.Response(jsonEncode(vote()), 200);
            }
            expect(request.url.path.endsWith('/vote'), isTrue);
            bodies.add(jsonDecode(request.body) as Map<String, dynamic>);
            return http.Response('{}', bodies.length == 1 ? 503 : 200);
          }));
      final session = IdentitySession(transport);
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
                                  client: WorkflowTodoClient(transport)))),
                      child: const Text('Open'))))));
      await tester.tap(find.text('Open'));
      await tester.pumpAndSettle();
      expect(find.text('Your private ballot form'), findsOneWidget);
      expect(find.text('Approve'), findsNothing);
      expect(
          find.text('Submit ballot'), allowed ? findsOneWidget : findsNothing);
      if (allowed) {
        await tester.enterText(
            find.byKey(const ValueKey('answer')), 'private-answer');
        await tester.tap(find.byKey(const ValueKey('$todoId:choice')));
        await tester.pumpAndSettle();
        await tester.tap(find.text('Yes').last);
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.text('Submit ballot'));
        await tester.tap(find.text('Submit ballot'));
        await tester.pumpAndSettle();
        expect(bodies.length, 1);
        await tester.tap(find.text('Submit ballot'));
        await tester.pumpAndSettle();
        expect(bodies.length, 2);
        expect(bodies[0], bodies[1]);
        expect(bodies[0]['expectedBallotRevision'], 2);
        expect(bodies[0]['values'], {'answer': 'private-answer'});
        expect(bodies[0].containsKey('expectedFormRevision'), isFalse);
        expect(find.text('Open'), findsOneWidget);
      } else {
        expect(bodies, isEmpty);
      }
    });
  }

  test('vote guard rejects mixed approval authority and early choice totals',
      () {
    expect(WorkflowTodoDetail.fromJson(vote()).voteForm!.revision, 2);
    expect(() => WorkflowTodoDetail.fromJson({...vote(), 'approvedCount': 1}),
        throwsFormatException);
    expect(
        () => WorkflowTodoDetail.fromJson({
              ...vote(),
              'allowedActions': ['approve']
            }),
        throwsFormatException);
    final early = vote();
    (early['voteProgress'] as Map<String, dynamic>)['yesCount'] = 1;
    expect(() => WorkflowTodoDetail.fromJson(early), throwsFormatException);
    final stale = vote();
    (stale['voteForm'] as Map<String, dynamic>)['revision'] = 0;
    expect(() => WorkflowTodoDetail.fromJson(stale), throwsFormatException);
  });

  test(
      'vote HTTP preserves decimal strings and has no shared form or authority fields',
      () async {
    final transport = FullNetHttpClient(
        apiBaseUrl: 'https://api.example.test',
        inner: MockClient((request) async {
          final body = jsonDecode(request.body) as Map<String, dynamic>;
          expect(body['values'], {'amount': '9007199254740993.12345678'});
          expect(
              body.keys,
              unorderedEquals([
                'expectedRevision',
                'expectedBallotRevision',
                'choiceKey',
                'values',
                'idempotencyKey',
                'comment'
              ]));
          return http.Response('{}', 200);
        }));
    await WorkflowTodoClient(transport).vote(
        todoId,
        const VoteWorkflowTodoRequest(
            expectedRevision: 1,
            expectedBallotRevision: 2,
            choiceKey: 'yes',
            values: {'amount': '9007199254740993.12345678'},
            idempotencyKey: 'decimal-vote'));
  });
}
