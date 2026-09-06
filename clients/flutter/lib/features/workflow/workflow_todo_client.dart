import '../../core/http/full_net_http_client.dart';
import 'workflow_models.dart';

/// 消费工作流待办 OpenAPI；所有响应在进入 UI 前执行运行时守卫。
class WorkflowTodoClient {
  WorkflowTodoClient(this._http);

  final FullNetHttpClient _http;

  Future<List<WorkflowTodoListItem>> listMine({
    int page = 1,
    int pageSize = 20,
  }) async {
    final value = await _http.getJson(
      '/api/v1/workflow/todos/mine?page=$page&pageSize=$pageSize',
    );
    return parseWorkflowTodoListPage(value);
  }

  Future<WorkflowTodoDetail> getRuntime(String todoId) async {
    final value = await _http.getJson('/api/v1/workflow/todos/$todoId/runtime');
    if (value is! Map<String, dynamic>) {
      throw const FormatException('WorkflowTodoRuntimeResponse must be an object');
    }
    return WorkflowTodoDetail.fromJson(value);
  }

  Future<void> approve(String todoId, ActWorkflowTodoRequest request) =>
      _act(todoId, 'approve', request);

  Future<void> reject(String todoId, ActWorkflowTodoRequest request) =>
      _act(todoId, 'reject', request);

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
