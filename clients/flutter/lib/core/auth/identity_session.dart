import '../http/full_net_http_client.dart';
import 'current_user.dart';

/// 内存中的密码登录会话；访问令牌不落本地存储。
class IdentitySession {
  IdentitySession(this._http);

  final FullNetHttpClient _http;
  CurrentUser? _currentUser;

  CurrentUser? get currentUser => _currentUser;

  bool get isAuthenticated => _currentUser != null;

  bool can(String permission) => _currentUser?.can(permission) ?? false;

  Future<void> login({
    required String username,
    required String password,
  }) async {
    final tokenJson = await _http.postJson(
      '/api/v1/auth/login',
      body: {
        'username': username,
        'password': password,
      },
    );
    if (tokenJson is! Map<String, dynamic>) {
      throw const FormatException('TokenResponse must be an object');
    }

    final accessToken = tokenJson['accessToken'];
    if (accessToken is! String || accessToken.isEmpty) {
      throw const FormatException('accessToken is required');
    }

    _http.setAccessToken(accessToken);
    await refreshCurrentUser();
  }

  Future<void> refreshCurrentUser() async {
    final value = await _http.getJson('/api/v1/me');
    if (value is! Map<String, dynamic>) {
      throw const FormatException('CurrentUserResponse must be an object');
    }
    _currentUser = CurrentUser.fromJson(value);
  }

  void logout() {
    _http.setAccessToken(null);
    _currentUser = null;
  }
}
