import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';

/// 轻量本地化入口；正式构建前由 `flutter gen-l10n` 生成同名类型。
class AppLocalizations {
  AppLocalizations(this.localeName);

  final String localeName;

  static const supportedLocales = [
    Locale('zh', 'CN'),
    Locale('en', 'US'),
  ];

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  static AppLocalizations of(BuildContext context) {
    final value = Localizations.of<AppLocalizations>(context, AppLocalizations);
    assert(value != null, 'AppLocalizations not found in context');
    return value!;
  }

  bool get _isEnglish => localeName.startsWith('en');

  String get appTitle => _isEnglish ? 'Full.NET' : 'Full.NET';
  String get loginTitle => _isEnglish ? 'Sign in to Full.NET' : '登录 Full.NET';
  String get loginDescription => _isEnglish
      ? 'Access your workflow tasks and approvals.'
      : '访问你的工作流待办与审批任务。';
  String get username => _isEnglish ? 'Username' : '用户名';
  String get password => _isEnglish ? 'Password' : '密码';
  String get signIn => _isEnglish ? 'Sign in' : '登录';
  String get signInFailed => _isEnglish
      ? 'Sign-in failed. Check your account or try again later.'
      : '登录失败，请检查账号或稍后重试。';
  String get workflowTodosTitle => _isEnglish ? 'My tasks' : '我的待办';
  String get workflowTodoTitle => _isEnglish ? 'Task details' : '待办详情';
  String get refresh => _isEnglish ? 'Refresh' : '刷新';
  String get emptyTodos =>
      _isEnglish ? 'There are no pending tasks.' : '当前没有待办任务。';
  String get permissionDenied => _isEnglish
      ? 'This account cannot read workflow tasks.'
      : '当前账号没有查看待办的权限。';
  String get loadFailed =>
      _isEnglish ? 'Tasks could not be loaded. Try again later.' : '待办加载失败，请稍后重试。';
  String get comment => _isEnglish ? 'Comment' : '处理意见';
  String get approve => _isEnglish ? 'Approve' : '同意';
  String get reject => _isEnglish ? 'Reject' : '驳回';
  String get submitting => _isEnglish ? 'Submitting…' : '正在提交…';
  String get completed => _isEnglish ? 'Task completed.' : '处理成功。';
  String get actionFailed => _isEnglish ? 'Task action failed.' : '审批操作失败。';
}

class _AppLocalizationsDelegate extends LocalizationsDelegate<AppLocalizations> {
  @override
  bool isSupported(Locale locale) =>
      AppLocalizations.supportedLocales.any(
        (supported) =>
            supported.languageCode == locale.languageCode &&
            (supported.countryCode == null ||
                supported.countryCode == locale.countryCode),
      );

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture(AppLocalizations(locale.toLanguageTag()));
  }

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}
