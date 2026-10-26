import 'workflow_claim_models.dart';
export 'workflow_claim_models.dart';
import 'workflow_vote_models.dart';
export 'workflow_vote_models.dart';

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
    required this.instanceId,
    required this.stepId,
    required this.assigneeUserId,
    required this.statusKey,
    required this.revision,
    required this.submissionRevision,
    required this.comment,
    required this.submission,
    required this.allowedActions,
    required this.signRequired,
    required this.commentRequired,
    required this.activityKindKey,
    this.taskProgress,
    this.voteForm,
    this.voteProgress,
    this.claimProgress,
  });

  final String id;
  final String instanceId;
  final String stepId;
  final String assigneeUserId;
  final String statusKey;
  final int revision;
  final int submissionRevision;
  final String? comment;
  final Map<String, dynamic> submission;
  final List<String> allowedActions;
  final bool signRequired;
  final bool commentRequired;
  final String activityKindKey;
  final WorkflowTaskProgress? taskProgress;
  final WorkflowVoteForm? voteForm;
  final WorkflowVoteProgress? voteProgress;
  final WorkflowClaimProgress? claimProgress;

  factory WorkflowTodoDetail.fromJson(Map<String, dynamic> json) {
    final submission = json['submission'];
    if (submission is! Map<String, dynamic>) {
      throw const FormatException('submission must be an object');
    }
    final actions = json['allowedActions'];
    final kind = _requireString(json, 'activityKindKey');
    if (!{
      'human.approval',
      'human.group_approval',
      'human.task',
      'human.vote',
      'human.claim'
    }.contains(kind)) {
      throw const FormatException('Invalid workflow activity kind');
    }
    const approvalActions = {
      'approve',
      'reject',
      'return',
      'countersign',
      'transfer',
      'delegate',
      'remind'
    };
    const taskActions = {
      'complete',
      'return',
      'transfer',
      'delegate',
      'remind'
    };
    const voteActions = {'vote', 'transfer', 'delegate', 'remind'};
    final knownActions = kind == 'human.claim'
        ? {'complete', 'release', 'transfer', 'delegate', 'remind'}
        : kind == 'human.task'
            ? taskActions
            : kind == 'human.vote'
                ? voteActions
                : approvalActions;
    if (actions is! List ||
        actions.any(
            (action) => action is! String || !knownActions.contains(action)) ||
        actions.toSet().length != actions.length ||
        json['signRequired'] is! bool ||
        json['commentRequired'] is! bool) {
      throw const FormatException('Invalid workflow action policy');
    }
    WorkflowClaimProgress? claimProgress;
    if (kind == 'human.claim') {
      if (json['claimProgress'] is! Map<String, dynamic> ||
          json['approvalModeKey'] != 'none' ||
          [
            'requiredApprovalCount',
            'approvedCount',
            'rejectedCount',
            'pendingCount'
          ].any((key) => json[key] != 0)) {
        throw const FormatException('Claim cannot contain approval facts');
      }
      claimProgress = WorkflowClaimProgress.fromJson(
          json['claimProgress'] as Map<String, dynamic>);
    } else if (json['claimProgress'] != null) {
      throw const FormatException('Unexpected claim progress');
    }
    WorkflowTaskProgress? progress;
    WorkflowVoteForm? voteForm;
    WorkflowVoteProgress? voteProgress;
    if (kind == 'human.vote') {
      if (json['voteForm'] is! Map<String, dynamic> ||
          json['voteProgress'] is! Map<String, dynamic> ||
          json['approvalModeKey'] != 'none' ||
          [
            'requiredApprovalCount',
            'approvedCount',
            'rejectedCount',
            'pendingCount'
          ].any((key) => json[key] != 0)) {
        throw const FormatException(
            'Vote must have private form and independent progress');
      }
      voteForm =
          WorkflowVoteForm.fromJson(json['voteForm'] as Map<String, dynamic>);
      voteProgress = WorkflowVoteProgress.fromJson(
          json['voteProgress'] as Map<String, dynamic>);
    } else if (json['voteForm'] != null || json['voteProgress'] != null) {
      throw const FormatException('Unexpected private ballot');
    }
    if (kind == 'human.task') {
      final value = json['taskProgress'];
      if (value is! Map<String, dynamic> ||
          json['approvalModeKey'] != 'none' ||
          [
            'requiredApprovalCount',
            'approvedCount',
            'rejectedCount',
            'pendingCount'
          ].any((key) => json[key] != 0)) {
        throw const FormatException('Task cannot contain approval counts');
      }
      progress = WorkflowTaskProgress.fromJson(value);
    } else if (json['taskProgress'] != null) {
      throw const FormatException('Approval cannot contain task progress');
    }

    return WorkflowTodoDetail(
      id: _requireGuid(json, 'id'),
      instanceId: _requireGuid(json, 'instanceId'),
      stepId: _requireGuid(json, 'stepId'),
      assigneeUserId: _requireGuid(json, 'assigneeUserId'),
      statusKey: _requireString(json, 'statusKey'),
      revision: _requireInt(json, 'revision'),
      submissionRevision: _requireInt(json, 'submissionRevision'),
      comment: json['comment']?.toString(),
      submission: Map<String, dynamic>.from(submission),
      allowedActions: List<String>.unmodifiable(actions.cast<String>()),
      signRequired: json['signRequired'] as bool,
      commentRequired: json['commentRequired'] as bool,
      activityKindKey: kind,
      taskProgress: progress,
      voteForm: voteForm,
      voteProgress: voteProgress,
      claimProgress: claimProgress,
    );
  }
}

/// 办理数量与审批票分开校验，固定人数和门槛不能由客户端推测。
class WorkflowTaskProgress {
  const WorkflowTaskProgress(
      this.completionModeKey,
      this.memberCount,
      this.requiredCompletionCount,
      this.completedCount,
      this.pendingCount,
      this.cancelledCount);
  final String completionModeKey;
  final int memberCount;
  final int requiredCompletionCount;
  final int completedCount;
  final int pendingCount;
  final int cancelledCount;

  factory WorkflowTaskProgress.fromJson(Map<String, dynamic> json) {
    final mode = _requireString(json, 'completionModeKey');
    final members = _requireInt(json, 'memberCount');
    final required = _requireInt(json, 'requiredCompletionCount');
    final completed = _requireInt(json, 'completedCount');
    final pending = _requireInt(json, 'pendingCount');
    final cancelled = _requireInt(json, 'cancelledCount');
    if (!{'all', 'any', 'sequential'}.contains(mode) ||
        members < 1 ||
        members > 20 ||
        required != (mode == 'any' ? 1 : members) ||
        completed < 0 ||
        pending < 0 ||
        cancelled < 0 ||
        completed + pending + cancelled != members) {
      throw const FormatException('Invalid task progress');
    }
    return WorkflowTaskProgress(
        mode, members, required, completed, pending, cancelled);
  }
}

/// 采集回执必须是当前协议的有界文件引用，不接受临时路径或图片 URL。
class WorkflowSignatureUpload {
  const WorkflowSignatureUpload(this.fileId);
  final String fileId;

  factory WorkflowSignatureUpload.fromJson(Map<String, dynamic> json) {
    final size = json['sizeBytes'];
    final hash = json['contentHash'];
    if (!json.containsKey('contentHash') ||
        size is! int ||
        size < 8 ||
        size > 1048576 ||
        (hash != null &&
            (hash is! String ||
                !RegExp(r'^[0-9a-fA-F]{64}$').hasMatch(hash)))) {
      throw const FormatException('Invalid signature upload response');
    }
    return WorkflowSignatureUpload(_requireGuid(json, 'fileId'));
  }
}

class ActWorkflowTodoRequest {
  const ActWorkflowTodoRequest({
    required this.expectedRevision,
    required this.expectedFormRevision,
    required this.fieldPatch,
    required this.idempotencyKey,
    this.comment,
    this.signatureFileId,
  });

  final int expectedRevision;
  final int expectedFormRevision;
  final Map<String, dynamic> fieldPatch;
  final String? comment;
  final String? signatureFileId;
  final String idempotencyKey;

  Map<String, dynamic> toJson() => {
        'expectedRevision': expectedRevision,
        'expectedFormRevision': expectedFormRevision,
        'fieldPatch': fieldPatch,
        'comment': comment,
        'signatureFileId': signatureFileId,
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
      .map(
          (item) => WorkflowTodoListItem.fromJson(item as Map<String, dynamic>))
      .toList();
}
