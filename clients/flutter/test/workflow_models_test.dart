import 'package:flutter_test/flutter_test.dart';

import 'package:fullnet_client/features/workflow/workflow_models.dart';

void main() {
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
