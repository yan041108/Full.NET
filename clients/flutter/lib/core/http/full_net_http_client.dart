import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

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

  /// 受保护附件使用应用内路径和当前凭据，错误响应继续按 ProblemDetails 解释。
  Future<Uint8List> getBytes(String path) async {
    if (!path.startsWith('/api/') || path.contains('//')) {
      throw ArgumentError('Invalid attachment path');
    }
    final response = await http.Response.fromStream(
        await _inner.send(http.Request('GET', _resolveUri(path))
          ..headers.addAll({
            'Accept': 'application/octet-stream',
            'Accept-Language': locale,
            if (_accessToken != null) 'Authorization': 'Bearer $_accessToken',
          })));
    if (response.statusCode >= 200 && response.statusCode <= 299) {
      return response.bodyBytes;
    }
    dynamic decoded;
    try {
      decoded = _jsonDecoder(utf8.decode(response.bodyBytes));
    } on FormatException {
      decoded = null;
    }
    throw tryParseHttpProblem(response.statusCode, decoded) ??
        HttpProblem(
            status: response.statusCode,
            code: 'http.unexpected_response',
            title: 'Request failed.');
  }

  Future<dynamic> postJson(String path, {Map<String, dynamic>? body}) =>
      _send('POST', path, body: body);

  /// 签名上传使用同一实时 Bearer 与语言，只接受应用内受控待办路径。
  Future<dynamic> postSignaturePng(String path, List<int> bytes) async {
    if (!path.startsWith('/api/v1/workflow/todos/') ||
        path.contains('//') ||
        bytes.length < 8 ||
        bytes.length > 1048576) {
      throw ArgumentError('Invalid signature upload');
    }
    final request = http.MultipartRequest('POST', _resolveUri(path))
      ..headers.addAll({
        'Accept': 'application/json',
        'Accept-Language': locale,
        if (_accessToken != null) 'Authorization': 'Bearer $_accessToken',
      })
      ..files.add(http.MultipartFile.fromBytes('file', bytes,
          filename: 'signature.png', contentType: MediaType('image', 'png')));
    return _readResponse(await _inner.send(request));
  }

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
    return _readResponse(response);
  }

  Future<dynamic> _readResponse(http.StreamedResponse response) async {
    final responseBody = await http.Response.fromStream(response);
    final decoded =
        responseBody.body.isEmpty ? null : _jsonDecoder(responseBody.body);

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
