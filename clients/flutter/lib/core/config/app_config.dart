/// 运行时 API 基址；通过 `--dart-define=FULLNET_API_BASE_URL=` 注入。
abstract final class AppConfig {
  static const apiBaseUrl = String.fromEnvironment(
    'FULLNET_API_BASE_URL',
    defaultValue: '',
  );

  static const preferredLocale = String.fromEnvironment(
    'FULLNET_LOCALE',
    defaultValue: 'zh-CN',
  );
}
