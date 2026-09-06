final RegExp _guidPattern = RegExp(
  r'^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
  caseSensitive: false,
);

/// 待办列表项；与 OpenAPI `WorkflowTodoListItemResponse` 对齐。
class WorkflowTodoListItem {
  const WorkflowTodoListItem({
    required this.id,
    required this.instanceId,
    required this.stepId,
    required this.statusKey,
    required this.arrivedAtUtc,
    required this.businessType,
    required this.businessId,
    required this.definitionKey,
    required this.revision,
  });

  final String id;
  final String instanceId;
  final String stepId;
  final String statusKey;
  final String arrivedAtUtc;
  final String businessType;
  final String businessId;
  final String definitionKey;
  final int revision;

  factory WorkflowTodoListItem.fromJson(Map<String, dynamic> json) {
    return WorkflowTodoListItem(
      id: _requireGuid(json, 'id'),
      instanceId: _requireGuid(json, 'instanceId'),
      stepId: _requireGuid(json, 'stepId'),
      statusKey: _requireString(json, 'statusKey'),
      arrivedAtUtc: _requireString(json, 'arrivedAtUtc'),
      businessType: _requireString(json, 'businessType'),
      businessId: _requireString(json, 'businessId'),
      definitionKey: _requireString(json, 'definitionKey'),
      revision: _requireInt(json, 'revision'),
    );
  }
}

/// 待办运行时详情；只校验移动端审批所需字段。
class WorkflowTodoDetail {
  const WorkflowTodoDetail({
    required this.id,
    required this.statusKey,
    required this.revision,
    required this.comment,
    required this.submission,
  });

  final String id;
  final String statusKey;
  final int revision;
  final String? comment;
  final Map<String, dynamic> submission;

  factory WorkflowTodoDetail.fromJson(Map<String, dynamic> json) {
    final submission = json['submission'];
    if (submission is! Map<String, dynamic>) {
      throw const FormatException('submission must be an object');
    }

    return WorkflowTodoDetail(
      id: _requireGuid(json, 'id'),
      statusKey: _requireString(json, 'statusKey'),
      revision: _requireInt(json, 'revision'),
      comment: json['comment']?.toString(),
      submission: Map<String, dynamic>.from(submission),
    );
  }
}

class ActWorkflowTodoRequest {
  const ActWorkflowTodoRequest({
    required this.expectedRevision,
    required this.fieldPatch,
    required this.idempotencyKey,
    this.comment,
  });

  final int expectedRevision;
  final Map<String, dynamic> fieldPatch;
  final String? comment;
  final String idempotencyKey;

  Map<String, dynamic> toJson() => {
        'expectedRevision': expectedRevision,
        'fieldPatch': fieldPatch,
        'comment': comment,
        'idempotencyKey': idempotencyKey,
      };
}

String _requireGuid(Map<String, dynamic> json, String key) {
  final value = _requireString(json, key);
  if (!_guidPattern.hasMatch(value)) {
    throw FormatException('$key must be a GUID');
  }
  return value.toLowerCase();
}

String _requireString(Map<String, dynamic> json, String key) {
  final value = json[key];
  if (value is! String || value.isEmpty) {
    throw FormatException('$key must be a non-empty string');
  }
  return value;
}

int _requireInt(Map<String, dynamic> json, String key) {
  final value = json[key];
  if (value is! int) {
    throw FormatException('$key must be an integer');
  }
  return value;
}

List<WorkflowTodoListItem> parseWorkflowTodoListPage(Object? value) {
  if (value is! Map<String, dynamic>) {
    throw const FormatException('PagedResult must be an object');
  }
  final items = value['items'];
  if (items is! List) {
    throw const FormatException('items must be an array');
  }
  return items
      .map((item) => WorkflowTodoListItem.fromJson(item as Map<String, dynamic>))
      .toList();
}
