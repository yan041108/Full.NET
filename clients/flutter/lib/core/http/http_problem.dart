/// 与 Full.NET ProblemDetails 对齐的最小 HTTP 错误模型。
class HttpProblem implements Exception {
  HttpProblem({
    required this.status,
    required this.code,
    required this.title,
    this.detail,
    this.traceId,
  });

  final int status;
  final String code;
  final String title;
  final String? detail;
  final String? traceId;

  @override
  String toString() => '$code: $title';
}

HttpProblem? tryParseHttpProblem(int status, Object? body) {
  if (body is! Map<String, dynamic>) {
    return null;
  }

  final title = body['title'];
  final type = body['type'];
  if (title is! String || type is! String) {
    return null;
  }

  final extensions = body['extensions'];
  final code = extensions is Map<String, dynamic>
      ? extensions['code']?.toString()
      : null;

  return HttpProblem(
    status: status,
    code: code ?? 'http.problem',
    title: title,
    detail: body['detail']?.toString(),
    traceId: extensions is Map<String, dynamic>
        ? extensions['traceId']?.toString()
        : null,
  );
}
