import 'workflow_models.dart';

/// 退回绑定真实历史步骤；多人活动没有单一历史办理人，不能由客户端补造。
class WorkflowReturnTarget {
  const WorkflowReturnTarget(this.stepId, this.nodeKey, this.completedAtUtc);
  final String stepId;
  final String nodeKey;
  final String completedAtUtc;
  static List<WorkflowReturnTarget> parsePage(dynamic value) {
    if (value is! List || value.length > 20) {
      throw const FormatException('Invalid return target page');
    }
    final seen = <String>{};
    return List.unmodifiable(value.map((dynamic item) {
      if (item is! Map<String, dynamic> ||
          item['nodeKey'] is! String ||
          !RegExp(r'^[A-Za-z][A-Za-z0-9_.-]{0,127}$')
              .hasMatch(item['nodeKey'] as String) ||
          item['completedAtUtc'] is! String ||
          DateTime.tryParse(item['completedAtUtc'] as String) == null) {
        throw const FormatException('Invalid return target');
      }
      final id = workflowClaimGuid(item['stepId']);
      if (!seen.add(id)) throw const FormatException('Duplicate return target');
      if (item['assigneeUserId'] != null) {
        workflowClaimGuid(item['assigneeUserId']);
      }
      return WorkflowReturnTarget(
          id, item['nodeKey'] as String, item['completedAtUtc'] as String);
    }));
  }
}

class WorkflowCountersignItem {
  const WorkflowCountersignItem(
      this.id, this.userId, this.sequence, this.status);
  final String id;
  final String userId;
  final int sequence;
  final String status;
}

/// 读取的活动链不能凭未知状态开放取消，次序和真实待办关联均验证后展示。
class WorkflowCountersignChain {
  const WorkflowCountersignChain(this.id, this.direction, this.items);
  final String id;
  final String direction;
  final List<WorkflowCountersignItem> items;
  factory WorkflowCountersignChain.parse(dynamic value) {
    if (value is! Map<String, dynamic> ||
        value['statusKey'] != 'active' ||
        !{'before', 'after'}.contains(value['directionKey']) ||
        value['items'] is! List) {
      throw const FormatException('Invalid countersign chain');
    }
    final rows = value['items'] as List;
    final ids = <String>{};
    final users = <String>{};
    final todos = <String>{};
    if (rows.isEmpty || rows.length > 10) {
      throw const FormatException('Invalid countersign length');
    }
    final result = <WorkflowCountersignItem>[];
    for (var index = 0; index < rows.length; index++) {
      final item = rows[index];
      if (item is! Map<String, dynamic> ||
          item['sequenceNo'] != index + 1 ||
          !{'pending', 'active', 'completed', 'cancelled'}
              .contains(item['statusKey'])) {
        throw const FormatException('Invalid countersign item');
      }
      final id = workflowClaimGuid(item['itemId']);
      final user = workflowClaimGuid(item['assigneeUserId']);
      if (!ids.add(id) || !users.add(user)) {
        throw const FormatException('Duplicate countersign item');
      }
      if (item['todoId'] != null) {
        if (!todos.add(workflowClaimGuid(item['todoId']))) {
          throw const FormatException('Duplicate countersign todo');
        }
      } else if ({'active', 'completed'}.contains(item['statusKey'])) {
        throw const FormatException('Countersign todo is required');
      }
      result.add(WorkflowCountersignItem(
          id, user, index + 1, item['statusKey'] as String));
    }
    return WorkflowCountersignChain(workflowClaimGuid(value['chainId']),
        value['directionKey'] as String, List.unmodifiable(result));
  }
}

class WorkflowReminderTarget {
  const WorkflowReminderTarget(this.stepId, this.revision, this.canRemind);
  final String stepId;
  final int revision;
  final bool canRemind;
}

class WorkflowReminderTargets {
  const WorkflowReminderTargets(this.instanceRevision, this.items);
  final int instanceRevision;
  final List<WorkflowReminderTarget> items;
  factory WorkflowReminderTargets.parse(dynamic value) {
    if (value is! Map<String, dynamic> ||
        value['instanceRevision'] is! int ||
        (value['instanceRevision'] as int) < 1 ||
        value['items'] is! List ||
        (value['items'] as List).length > 512) {
      throw const FormatException('Invalid reminder targets');
    }
    final seen = <String>{};
    final result = <WorkflowReminderTarget>[];
    for (final dynamic item in value['items'] as List) {
      if (item is! Map<String, dynamic> ||
          item['stepRevision'] is! int ||
          (item['stepRevision'] as int) < 1 ||
          item['canRemind'] is! bool ||
          !{
            'human.approval',
            'human.group_approval',
            'human.task',
            'human.vote',
            'human.claim'
          }.contains(item['nodeTypeKey']) ||
          item['nodeKey'] is! String ||
          (item['nodeKey'] as String).isEmpty ||
          item['activeTodoCount'] is! int ||
          (item['activeTodoCount'] as int) < 1 ||
          (item['activeTodoCount'] as int) > 20 ||
          item['manualCount'] is! int ||
          (item['manualCount'] as int) < 0 ||
          (item['manualCount'] as int) > 20 ||
          (item['manualCount'] == 0) != (item['lastRequestedAtUtc'] == null) ||
          item['manualCount'] == 20 && item['canRemind'] == true) {
        throw const FormatException('Invalid reminder target');
      }
      if (item['lastRequestedAtUtc'] != null &&
          (item['lastRequestedAtUtc'] is! String ||
              DateTime.tryParse(item['lastRequestedAtUtc'] as String) ==
                  null)) {
        throw const FormatException('Invalid reminder time');
      }
      final id = workflowClaimGuid(item['stepId']);
      if (!seen.add(id)) throw const FormatException('Duplicate reminder step');
      result.add(WorkflowReminderTarget(
          id, item['stepRevision'] as int, item['canRemind'] as bool));
    }
    return WorkflowReminderTargets(
        value['instanceRevision'] as int, List.unmodifiable(result));
  }
}

/// 管理动作可以耐久续办而暂无Todo，终态回执不能虚构新的待办。
void validateWorkflowManagementReceipt(dynamic value) {
  if (value is! Map<String, dynamic> ||
      value['revision'] is! int ||
      (value['revision'] as int) < 1 ||
      !{'active', 'suspended', 'completed', 'rejected', 'cancelled'}
          .contains(value['statusKey']) ||
      value['startedAtUtc'] is! String ||
      DateTime.tryParse(value['startedAtUtc'] as String) == null) {
    throw const FormatException('Invalid workflow action receipt');
  }
  for (final field in ['id', 'definitionVersionId', 'formVersionId']) {
    workflowClaimGuid(value[field]);
  }
  if (value['activeTodoId'] != null) {
    workflowClaimGuid(value['activeTodoId']);
    if ({'completed', 'rejected', 'cancelled'}.contains(value['statusKey'])) {
      throw const FormatException('Terminal receipt contains active todo');
    }
  }
}
