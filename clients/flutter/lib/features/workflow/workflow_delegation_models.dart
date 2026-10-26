import 'workflow_models.dart';

/// 单待办代理事实；可收回标志不能脱离原始身份、当前状态及表单修订使用。
class WorkflowTodoDelegation {
  const WorkflowTodoDelegation(
      this.originTodoId,
      this.proxyTodoId,
      this.originalAssigneeUserId,
      this.proxyUserId,
      this.status,
      this.proxyRevision,
      this.formRevision,
      this.canRecall);
  final String originTodoId;
  final String proxyTodoId;
  final String originalAssigneeUserId;
  final String proxyUserId;
  final String status;
  final int proxyRevision;
  final int formRevision;
  final bool canRecall;

  static WorkflowTodoDelegation? parse(dynamic value) {
    if (value is! Map<String, dynamic>) {
      throw const FormatException('Invalid delegation state');
    }
    final grant = value['delegation'];
    if (grant == null) return null;
    if (grant is! Map<String, dynamic>) {
      throw const FormatException('Invalid delegation');
    }
    final origin = workflowClaimGuid(grant['originTodoId']);
    final proxy = workflowClaimGuid(grant['proxyTodoId']);
    final original = workflowClaimGuid(grant['originalAssigneeUserId']);
    final actor = workflowClaimGuid(grant['proxyUserId']);
    final status = grant['statusKey'];
    final revision = grant['proxyRevision'];
    final form = grant['formRevision'];
    if (origin == proxy ||
        original == actor ||
        !{'active', 'completed', 'recalled', 'reassigned', 'cancelled'}
            .contains(status) ||
        revision is! int ||
        revision < 1 ||
        form is! int ||
        form < 0 ||
        grant['canRecall'] is! bool ||
        grant['createdAtUtc'] is! String ||
        DateTime.tryParse(grant['createdAtUtc'] as String) == null ||
        grant['canRecall'] == true && (status != 'active' || form < 1)) {
      throw const FormatException('Invalid delegation authority');
    }
    return WorkflowTodoDelegation(origin, proxy, original, actor,
        status as String, revision, form, grant['canRecall'] as bool);
  }
}

class WorkflowDelegateCandidate {
  const WorkflowDelegateCandidate(this.id, this.label);
  final String id;
  final String label;
}

/// 真实20条目录分页，不把当前页当作完整候选集。
class WorkflowDelegateCandidatePage {
  const WorkflowDelegateCandidatePage(this.items, this.page, this.total);
  final List<WorkflowDelegateCandidate> items;
  final int page;
  final int total;
  factory WorkflowDelegateCandidatePage.parse(dynamic value) {
    _validatePage(value);
    final rows = value['items'];
    if (rows is! List || rows.length > 20) {
      throw const FormatException('Invalid delegation candidates');
    }
    final candidates = <WorkflowDelegateCandidate>[];
    for (final row in rows) {
      if (row is! Map<String, dynamic> ||
          row['username'] is! String ||
          row['displayName'] is! String) {
        throw const FormatException('Invalid delegation candidate');
      }
      final name = row['displayName'] as String;
      candidates.add(WorkflowDelegateCandidate(workflowClaimGuid(row['id']),
          name.isEmpty ? row['username'] as String : name));
    }
    if (candidates.map((item) => item.id).toSet().length != candidates.length) {
      throw const FormatException('Duplicate delegation candidate');
    }
    return WorkflowDelegateCandidatePage(List.unmodifiable(candidates),
        value['page'] as int, value['total'] as int);
  }
}

class WorkflowDelegatedTodoPage {
  const WorkflowDelegatedTodoPage(this.items, this.page, this.total);
  final List<WorkflowTodoListItem> items;
  final int page;
  final int total;
  factory WorkflowDelegatedTodoPage.parse(dynamic value) {
    _validatePage(value);
    return WorkflowDelegatedTodoPage(
        List.unmodifiable(parseWorkflowTodoListPage(value)),
        value['page'] as int,
        value['total'] as int);
  }
}

void _validatePage(dynamic value) {
  if (value is! Map<String, dynamic> ||
      value['page'] is! int ||
      (value['page'] as int) < 1 ||
      value['pageSize'] != 20 ||
      value['total'] is! int ||
      (value['total'] as int) < 0) {
    throw const FormatException('Invalid delegation page');
  }
}

/// 代理和收回均使用相同WorkflowInstanceResponse固定回执，必须包含新的实际活动待办。
String parseWorkflowDelegationReceipt(dynamic value) =>
    parseWorkflowClaimReceipt(value, released: false)!;
