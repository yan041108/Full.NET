import 'package:flutter_test/flutter_test.dart';

import 'package:fullnet_client/features/workflow/workflow_models.dart';

void main() {
  for (final kind in ['human.approval', 'human.group_approval']) {
    test('delegation action does not make a $kind todo unreadable', () {
      final detail = WorkflowTodoDetail.fromJson({
        'id': '01912345-6789-7abc-8def-0123456789ab',
        'instanceId': '01912345-6789-7abc-8def-0123456789ac',
        'stepId': '01912345-6789-7abc-8def-0123456789ad',
        'assigneeUserId': '01912345-6789-7abc-8def-0123456789ae',
        'statusKey': 'active',
        'revision': 1,
        'submissionRevision': 1,
        'submission': <String, dynamic>{},
        'activityKindKey': kind,
        'allowedActions': ['approve', 'delegate'],
        'signRequired': false,
        'commentRequired': false
      });
      expect(detail.allowedActions, ['approve', 'delegate']);
    });
  }
  test('parseWorkflowTodoListPage guards list items', () {
    final items = parseWorkflowTodoListPage({
      'items': [
        {
          'id': '01912345-6789-7abc-8def-0123456789ab',
          'instanceId': '01912345-6789-7abc-8def-0123456789ac',
          'stepId': '01912345-6789-7abc-8def-0123456789ad',
          'statusKey': 'active',
          'arrivedAtUtc': '2026-09-07T00:00:00Z',
          'businessType': 'leave',
          'businessId': 'leave-1',
          'definitionKey': 'leave.approval',
          'revision': 3,
        },
      ],
      'page': 1,
      'pageSize': 20,
      'total': 1,
    });

    expect(items, hasLength(1));
    expect(items.first.statusKey, 'active');
  });
}
