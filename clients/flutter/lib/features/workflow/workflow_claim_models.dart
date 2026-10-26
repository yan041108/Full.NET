/// 领取池只公开固定候选本人可见的工作项，当前办理人与租户不能从客户端覆盖。
class WorkflowClaimPool {
  const WorkflowClaimPool(this.id, this.instanceId, this.title, this.revision,
      this.claimCycle, this.createdAtUtc, this.dueAtUtc);
  final String id, instanceId, title, createdAtUtc;
  final String? dueAtUtc;
  final int revision, claimCycle;
  factory WorkflowClaimPool.fromJson(Map<String, dynamic> json) {
    final id = workflowClaimGuid(json['id']);
    final instance = workflowClaimGuid(json['instanceId']);
    final revision = json['revision'], cycle = json['claimCycle'];
    if (revision is! int ||
        revision < 1 ||
        cycle is! int ||
        cycle < 0 ||
        cycle >= 32 ||
        json['createdAtUtc'] is! String ||
        DateTime.tryParse(json['createdAtUtc'] as String) == null ||
        json['dueAtUtc'] != null &&
            (json['dueAtUtc'] is! String ||
                DateTime.tryParse(json['dueAtUtc'] as String) == null) ||
        json['businessType'] is! String ||
        json['businessId'] is! String ||
        json['businessTitle'] != null && json['businessTitle'] is! String) {
      throw const FormatException('Invalid claim pool');
    }
    return WorkflowClaimPool(
        id,
        instance,
        json['businessTitle'] as String? ??
            '${json['businessType']} · ${json['businessId']}',
        revision,
        cycle,
        json['createdAtUtc'] as String,
        json['dueAtUtc'] as String?);
  }
}

/// 释放只绑定本人的池修订与实际代次，耗尽预算时不能重新开放领取。
class WorkflowClaimProgress {
  const WorkflowClaimProgress(
      this.status, this.revision, this.cycle, this.allowRelease);
  final String status;
  final int revision, cycle;
  final bool allowRelease;
  factory WorkflowClaimProgress.fromJson(Map<String, dynamic> json) {
    final status = json['poolStatusKey'],
        revision = json['poolRevision'],
        cycle = json['claimCycle'],
        release = json['allowRelease'];
    if (!{'available', 'claimed', 'completed', 'cancelled'}.contains(status) ||
        revision is! int ||
        revision < 1 ||
        cycle is! int ||
        cycle < 0 ||
        cycle > 32 ||
        release is! bool ||
        (status == 'claimed' || status == 'completed') && cycle < 1 ||
        status == 'available' && cycle >= 32 ||
        release && cycle >= 32) {
      throw const FormatException('Invalid claim progress');
    }
    return WorkflowClaimProgress(status as String, revision, cycle, release);
  }
}

List<WorkflowClaimPool> parseWorkflowClaimPools(dynamic value) {
  if (value is! Map<String, dynamic> ||
      value['items'] is! List ||
      value['page'] is! int ||
      (value['page'] as int) < 1 ||
      value['pageSize'] is! int ||
      (value['pageSize'] as int) < 1 ||
      (value['pageSize'] as int) > 100 ||
      value['totalCount'] is! int ||
      (value['totalCount'] as int) < (value['items'] as List).length ||
      (value['items'] as List).length > (value['pageSize'] as int)) {
    throw const FormatException('Invalid claim pool page');
  }
  return (value['items'] as List).map((item) {
    if (item is! Map<String, dynamic>) {
      throw const FormatException('Invalid claim pool row');
    }
    return WorkflowClaimPool.fromJson(item);
  }).toList(growable: false);
}

String workflowClaimGuid(dynamic value) {
  if (value is! String ||
      !RegExp(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$')
          .hasMatch(value)) {
    throw const FormatException('Invalid workflow resource identifier');
  }
  return value;
}

/// 领取回执使用服务端固定的实际Todo；释放回执必须没有活动Todo。
String? parseWorkflowClaimReceipt(dynamic value, {required bool released}) {
  if (value is! Map<String, dynamic> ||
      value['statusKey'] != 'active' ||
      value['revision'] is! int ||
      (value['revision'] as int) < 1) {
    throw const FormatException('Invalid claim action receipt');
  }
  workflowClaimGuid(value['id']);
  workflowClaimGuid(value['definitionVersionId']);
  workflowClaimGuid(value['formVersionId']);
  if (!value.containsKey('activeTodoId') ||
      value['businessType'] is! String ||
      value['businessId'] is! String ||
      value['businessTitle'] != null && value['businessTitle'] is! String ||
      value['startedAtUtc'] is! String ||
      DateTime.tryParse(value['startedAtUtc'] as String) == null) {
    throw const FormatException('Invalid claim receipt metadata');
  }
  if (released) {
    if (value['activeTodoId'] != null) {
      throw const FormatException('Release cannot return an assigned todo');
    }
    return null;
  }
  return workflowClaimGuid(value['activeTodoId']);
}
