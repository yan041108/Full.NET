/// 当前用户摘要；字段名与 OpenAPI `CurrentUserResponse` 对齐。
class CurrentUser {
  const CurrentUser({
    required this.id,
    required this.username,
    required this.displayName,
    required this.permissions,
    required this.preferredLocale,
    required this.profileVersion,
    required this.passwordChangeRequired,
  });

  final String id;
  final String username;
  final String displayName;
  final List<String> permissions;
  final String preferredLocale;
  final int profileVersion;
  final bool passwordChangeRequired;

  bool can(String permission) => permissions.contains(permission);

  factory CurrentUser.fromJson(Map<String, dynamic> json) {
    final permissions = json['permissions'];
    if (permissions is! List) {
      throw const FormatException('permissions must be an array');
    }

    return CurrentUser(
      id: _requireString(json, 'id'),
      username: _requireString(json, 'username'),
      displayName: _requireString(json, 'displayName'),
      permissions: permissions.map((item) => item.toString()).toList(),
      preferredLocale: _requireString(json, 'preferredLocale'),
      profileVersion: _requireInt(json, 'profileVersion'),
      passwordChangeRequired: json['passwordChangeRequired'] == true,
    );
  }
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
