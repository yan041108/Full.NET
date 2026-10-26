import 'dart:typed_data';
import '../../core/http/full_net_http_client.dart';
import 'workflow_models.dart';
import 'workflow_delegation_models.dart';
import 'workflow_human_management_models.dart';
import '../../core/http/http_problem.dart';

/// 消费工作流待办 OpenAPI；所有响应在进入 UI 前执行运行时守卫。
class WorkflowTodoClient {
  WorkflowTodoClient(this._http);

  final FullNetHttpClient _http;

  Future<WorkflowDelegatedTodoPage> listHistory({int page = 1}) async {
    _validateDelegationPage(page);
    return WorkflowDelegatedTodoPage.parse(await _http
        .getJson('/api/v1/workflow/todos/mine/history?page=$page&pageSize=20'));
  }

  Future<WorkflowDelegateCandidatePage> listManagementCandidates(
      String todoId, String action,
      {int page = 1}) async {
    workflowClaimGuid(todoId);
    _validateDelegationPage(page);
    if (!{'transfer', 'countersign'}.contains(action)) {
      throw ArgumentError('Invalid workflow management action');
    }
    return WorkflowDelegateCandidatePage.parse(await _http.getJson(
        '/api/v1/workflow/todos/$todoId/$action-candidates?page=$page&pageSize=20'));
  }

  Future<List<WorkflowReturnTarget>> listReturnTargets(String todoId,
      {int page = 1}) async {
    workflowClaimGuid(todoId);
    _validateDelegationPage(page);
    return WorkflowReturnTarget.parsePage(await _http.getJson(
        '/api/v1/workflow/todos/$todoId/return-targets?page=$page&pageSize=20'));
  }

  Future<WorkflowCountersignChain?> getCountersignChain(String todoId) async {
    workflowClaimGuid(todoId);
    try {
      return WorkflowCountersignChain.parse(await _http
          .getJson('/api/v1/workflow/todos/$todoId/countersign-chain'));
    } on HttpProblem catch (error) {
      if (error.status == 404 &&
          error.code == 'workflow.todo.countersign_chain_not_found') {
        return null;
      }
      rethrow;
    }
  }

  Future<void> transfer(String todoId, String target,
      {required int revision,
      required int formRevision,
      required String key,
      String? comment,
      String? signatureFileId}) async {
    workflowClaimGuid(todoId);
    workflowClaimGuid(target);
    _validateManagementEvidence(
        revision, formRevision, key, comment, signatureFileId);
    validateWorkflowManagementReceipt(
        await _http.postJson('/api/v1/workflow/todos/$todoId/transfer', body: {
      'assigneeUserId': target,
      'expectedRevision': revision,
      'expectedFormRevision': formRevision,
      'idempotencyKey': key,
      if (comment != null) 'comment': comment,
      if (signatureFileId != null) 'signatureFileId': signatureFileId
    }));
  }

  Future<void> returnTodo(String todoId, String targetStep,
      {required int revision,
      required int formRevision,
      required String key,
      required String comment,
      String? signatureFileId}) async {
    workflowClaimGuid(todoId);
    workflowClaimGuid(targetStep);
    _validateManagementEvidence(
        revision, formRevision, key, comment, signatureFileId);
    if (comment.trim().isEmpty) {
      throw ArgumentError('A return reason is required');
    }
    validateWorkflowManagementReceipt(
        await _http.postJson('/api/v1/workflow/todos/$todoId/return', body: {
      'targetStepId': targetStep,
      'expectedRevision': revision,
      'expectedFormRevision': formRevision,
      'fieldPatch': <String, dynamic>{},
      'idempotencyKey': key,
      'comment': comment,
      if (signatureFileId != null) 'signatureFileId': signatureFileId
    }));
  }

  Future<void> countersign(
      String todoId, String direction, List<String> targets,
      {required int revision, required String key, String? comment}) async {
    workflowClaimGuid(todoId);
    _validateClaimOperation(revision, key);
    if (!{'before', 'after'}.contains(direction) ||
        targets.isEmpty ||
        targets.length > 10 ||
        targets.toSet().length != targets.length ||
        comment != null && comment.length > 512) {
      throw ArgumentError('Invalid countersign request');
    }
    for (final id in targets) {
      workflowClaimGuid(id);
    }
    validateWorkflowManagementReceipt(await _http
        .postJson('/api/v1/workflow/todos/$todoId/countersign', body: {
      'directionKey': direction,
      'assigneeUserIds': targets,
      'expectedRevision': revision,
      'idempotencyKey': key,
      if (comment != null) 'comment': comment
    }));
  }

  Future<void> cancelCountersign(String todoId,
      {required int revision, required String key, String? comment}) async {
    workflowClaimGuid(todoId);
    _validateClaimOperation(revision, key);
    if (comment != null && comment.length > 512) {
      throw ArgumentError('Invalid countersign reason');
    }
    validateWorkflowManagementReceipt(await _http
        .postJson('/api/v1/workflow/todos/$todoId/countersign/cancel', body: {
      'expectedRevision': revision,
      'idempotencyKey': key,
      if (comment != null) 'comment': comment
    }));
  }

  Future<WorkflowReminderTargets> reminderTargets(String instanceId) async {
    workflowClaimGuid(instanceId);
    return WorkflowReminderTargets.parse(await _http
        .getJson('/api/v1/workflow/instances/$instanceId/reminder-targets'));
  }

  Future<void> remind(String instanceId, String stepId,
      {required int instanceRevision,
      required int stepRevision,
      required String key}) async {
    workflowClaimGuid(instanceId);
    workflowClaimGuid(stepId);
    _validateClaimOperation(instanceRevision, key);
    if (stepRevision < 1) throw ArgumentError('Invalid step revision');
    final dynamic value = await _http
        .postJson('/api/v1/workflow/instances/$instanceId/remind', body: {
      'stepId': stepId,
      'expectedInstanceRevision': instanceRevision,
      'expectedStepRevision': stepRevision,
      'idempotencyKey': key
    });
    if (value is! Map<String, dynamic> ||
        value['stepId'] != stepId ||
        value['instanceRevision'] != instanceRevision ||
        value['stepRevision'] != stepRevision ||
        value['manualSequence'] is! int ||
        (value['manualSequence'] as int) < 1 ||
        (value['manualSequence'] as int) > 20 ||
        value['recipientCount'] is! int ||
        (value['recipientCount'] as int) < 1 ||
        (value['recipientCount'] as int) > 20 ||
        value['acceptedAtUtc'] is! String ||
        DateTime.tryParse(value['acceptedAtUtc'] as String) == null) {
      throw const FormatException('Invalid reminder receipt');
    }
    workflowClaimGuid(value['requestId']);
  }

  void _validateManagementEvidence(int revision, int formRevision, String key,
      String? comment, String? signature) {
    _validateClaimOperation(revision, key);
    if (formRevision < 1 || comment != null && comment.length > 512) {
      throw ArgumentError('Invalid management evidence');
    }
    if (signature != null) workflowClaimGuid(signature);
  }

  Future<WorkflowDelegatedTodoPage> listDelegated({int page = 1}) async {
    _validateDelegationPage(page);
    return WorkflowDelegatedTodoPage.parse(await _http.getJson(
        '/api/v1/workflow/todos/mine/history?resultActionKey=delegate&page=$page&pageSize=20'));
  }

  Future<WorkflowDelegateCandidatePage> listDelegationCandidates(String todoId,
      {int page = 1}) async {
    workflowClaimGuid(todoId);
    _validateDelegationPage(page);
    return WorkflowDelegateCandidatePage.parse(await _http.getJson(
        '/api/v1/workflow/todos/$todoId/delegation-candidates?page=$page&pageSize=20'));
  }

  Future<WorkflowTodoDelegation?> getDelegation(String todoId) async {
    workflowClaimGuid(todoId);
    return WorkflowTodoDelegation.parse(
        await _http.getJson('/api/v1/workflow/todos/$todoId/delegation'));
  }

  /// 交接仅提交明确授权目标与证据，不附带表单字段或原办理人声明。
  Future<String> delegate(String todoId, String assigneeId,
      {required int revision,
      required int formRevision,
      required String key,
      String? comment,
      String? signatureFileId}) async {
    workflowClaimGuid(todoId);
    workflowClaimGuid(assigneeId);
    _validateClaimOperation(revision, key);
    if (formRevision < 1 || comment != null && comment.length > 512) {
      throw ArgumentError('Invalid delegation evidence');
    }
    if (signatureFileId != null) workflowClaimGuid(signatureFileId);
    return parseWorkflowDelegationReceipt(
        await _http.postJson('/api/v1/workflow/todos/$todoId/delegate', body: {
      'assigneeUserId': assigneeId,
      'expectedRevision': revision,
      'expectedFormRevision': formRevision,
      'idempotencyKey': key,
      if (comment != null) 'comment': comment,
      if (signatureFileId != null) 'signatureFileId': signatureFileId
    }));
  }

  Future<String> recallDelegation(String todoId,
      {required int revision,
      required int formRevision,
      required String key,
      String? comment}) async {
    workflowClaimGuid(todoId);
    _validateClaimOperation(revision, key);
    if (formRevision < 1 || comment != null && comment.length > 512) {
      throw ArgumentError('Invalid recall evidence');
    }
    return parseWorkflowDelegationReceipt(await _http
        .postJson('/api/v1/workflow/todos/$todoId/delegation/recall', body: {
      'expectedRevision': revision,
      'expectedFormRevision': formRevision,
      'idempotencyKey': key,
      if (comment != null) 'comment': comment
    }));
  }

  void _validateDelegationPage(int page) {
    if (page < 1 || page > 2147483647 ~/ 20) {
      throw ArgumentError('Invalid delegation page');
    }
  }

  /// 附件读取只接受实际待办和文件标识，实例查询能力不能替代个人视图授权。
  Future<Uint8List> downloadAttachment(String todoId, String fileId) async {
    workflowClaimGuid(todoId);
    workflowClaimGuid(fileId);
    return _http.getBytes(
        '/api/v1/workflow/todos/$todoId/form-attachments/$fileId/content');
  }

  /// 采集成功只返回不透明文件标识，后续动作仍由服务端绑定实际证据。
  Future<String> uploadSignature(
      String todoId, int expectedRevision, List<int> bytes) async {
    if (!RegExp(
            r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$')
        .hasMatch(todoId)) {
      throw ArgumentError('Invalid todo identifier');
    }
    final value = await _http.postSignaturePng(
        '/api/v1/workflow/todos/$todoId/signature-uploads?expectedRevision=$expectedRevision',
        bytes);
    if (value is! Map<String, dynamic>) {
      throw const FormatException('Invalid signature upload response');
    }
    return WorkflowSignatureUpload.fromJson(value).fileId;
  }

  Future<List<WorkflowTodoListItem>> listMine({
    int page = 1,
    int pageSize = 20,
  }) async {
    final value = await _http.getJson(
      '/api/v1/workflow/todos/mine?page=$page&pageSize=$pageSize',
    );
    return parseWorkflowTodoListPage(value);
  }

  Future<List<WorkflowClaimPool>> listClaimPools() async =>
      parseWorkflowClaimPools(await _http
          .getJson('/api/v1/workflow/claim-pools?page=1&pageSize=100'));

  Future<String> claimPool(String poolId, int revision, String key) async {
    workflowClaimGuid(poolId);
    _validateClaimOperation(revision, key);
    return parseWorkflowClaimReceipt(
        await _http.postJson('/api/v1/workflow/claim-pools/$poolId/claim',
            body: {'expectedRevision': revision, 'idempotencyKey': key}),
        released: false)!;
  }

  Future<void> release(String todoId,
      {required int revision,
      required int poolRevision,
      required int formRevision,
      required String key,
      String? comment,
      String? signatureFileId}) async {
    workflowClaimGuid(todoId);
    _validateClaimOperation(revision, key);
    if (poolRevision < 1 ||
        formRevision < 1 ||
        comment != null && comment.length > 512) {
      throw ArgumentError('Invalid release revisions or comment');
    }
    if (signatureFileId != null) workflowClaimGuid(signatureFileId);
    parseWorkflowClaimReceipt(
        await _http.postJson('/api/v1/workflow/todos/$todoId/release', body: {
          'expectedRevision': revision,
          'expectedPoolRevision': poolRevision,
          'expectedFormRevision': formRevision,
          'idempotencyKey': key,
          if (comment != null) 'comment': comment,
          if (signatureFileId != null) 'signatureFileId': signatureFileId,
        }),
        released: true);
  }

  void _validateClaimOperation(int revision, String key) {
    if (revision < 1 || key.trim().isEmpty || key.length > 128) {
      throw ArgumentError('Invalid claim operation');
    }
  }

  Future<WorkflowTodoDetail> getRuntime(String todoId) async {
    final value = await _http.getJson('/api/v1/workflow/todos/$todoId/runtime');
    if (value is! Map<String, dynamic>) {
      throw const FormatException(
          'WorkflowTodoRuntimeResponse must be an object');
    }
    return WorkflowTodoDetail.fromJson(value);
  }

  Future<void> approve(String todoId, ActWorkflowTodoRequest request) =>
      _act(todoId, 'approve', request);

  Future<void> reject(String todoId, ActWorkflowTodoRequest request) =>
      _act(todoId, 'reject', request);

  Future<void> complete(String todoId, ActWorkflowTodoRequest request) =>
      _act(todoId, 'complete', request);

  Future<void> vote(String todoId, VoteWorkflowTodoRequest request) async {
    if (!RegExp(
            r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-8][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}$')
        .hasMatch(todoId)) {
      throw ArgumentError('Invalid todo identifier');
    }
    await _http.postJson('/api/v1/workflow/todos/$todoId/vote',
        body: request.toJson());
  }

  Future<void> _act(
    String todoId,
    String action,
    ActWorkflowTodoRequest request,
  ) async {
    await _http.postJson(
      '/api/v1/workflow/todos/$todoId/$action',
      body: request.toJson(),
    );
  }
}
