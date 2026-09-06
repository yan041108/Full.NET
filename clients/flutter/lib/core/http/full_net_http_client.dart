import 'dart:convert';

import 'package:http/http.dart' as http;

import '../config/app_config.dart';
import 'http_problem.dart';

typedef JsonDecoder = dynamic Function(String source);

/// 轻量 HTTP 客户端：注入 Bearer、Accept-Language，并把 ProblemDetails 失败关闭。
class FullNetHttpClient {
  FullNetHttpClient({
    http.Client? inner,
    String? apiBaseUrl,
    this.locale = AppConfig.preferredLocale,
    String? accessToken,
    JsonDecoder? jsonDecoder,
  })  : _inner = inner ?? http.Client(),
        _apiBaseUrl = apiBaseUrl ?? AppConfig.apiBaseUrl,
        _accessToken = accessToken,
        _jsonDecoder = jsonDecoder ?? jsonDecode;

  final http.Client _inner;
  final String _apiBaseUrl;
  final JsonDecoder _jsonDecoder;
  String? _accessToken;
  final String locale;

  void setAccessToken(String? token) {
    _accessToken = token?.trim().isEmpty == true ? null : token?.trim();
  }

  Future<dynamic> getJson(String path) => _send('GET', path);

  Future<dynamic> postJson(String path, {Map<String, dynamic>? body}) =>
      _send('POST', path, body: body);

  Future<dynamic> _send(
    String method,
    String path, {
    Map<String, dynamic>? body,
  }) async {
    final uri = _resolveUri(path);
    final headers = <String, String>{
      'Accept': 'application/json',
      'Accept-Language': locale,
      if (body != null) 'Content-Type': 'application/json',
      if (_accessToken != null) 'Authorization': 'Bearer $_accessToken',
    };

    final response = await _inner.send(
      http.Request(method, uri)
        ..headers.addAll(headers)
        ..body = body == null ? '' : jsonEncode(body),
    );
    final responseBody = await http.Response.fromStream(response);
    final decoded = responseBody.body.isEmpty
        ? null
        : _jsonDecoder(responseBody.body);

    if (responseBody.statusCode >= 200 && responseBody.statusCode <= 299) {
      return decoded;
    }

    final problem = tryParseHttpProblem(responseBody.statusCode, decoded);
    throw problem ??
        HttpProblem(
          status: responseBody.statusCode,
          code: 'http.unexpected_response',
          title: 'Request failed.',
        );
  }

  Uri _resolveUri(String path) {
    if (path.startsWith('http://') || path.startsWith('https://')) {
      return Uri.parse(path);
    }

    final base = _apiBaseUrl.replaceAll(RegExp(r'/+$'), '');
    final normalizedPath = path.replaceFirst(RegExp(r'^/+'), '');
    if (base.isEmpty) {
      return Uri.parse('/$normalizedPath');
    }
    return Uri.parse('$base/$normalizedPath');
  }

  void close() => _inner.close();
}
