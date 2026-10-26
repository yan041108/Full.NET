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
  String get loadFailed => _isEnglish
      ? 'Tasks could not be loaded. Try again later.'
      : '待办加载失败，请稍后重试。';
  String get pendingTasks => _isEnglish ? 'Pending' : '待办';
  String get delegatedTodos => _isEnglish ? 'Delegated tasks' : '我委托的待办';

  /// 动作与状态仅接受协议守卫认可的机器码，译文不参与权限判断。
  String managementLabel(String key) {
    const zh = {
      'history': '已办历史',
      'choose': '请选择目标',
      'transfer': '转办',
      'return': '退回',
      'countersign': '加签',
      'before': '前加签',
      'after': '后加签',
      'add': '加入加签顺序',
      'remove': '移除',
      'chain': '当前加签链',
      'cancel': '取消加签',
      'remind': '催办',
      'unsaved': '请先刷新并恢复未保存的表单内容，再执行管理动作。',
      'pending': '待激活',
      'active': '办理中',
      'completed': '已完成',
      'cancelled': '已取消'
    };
    const en = {
      'history': 'Completed history',
      'choose': 'Choose a target',
      'transfer': 'Transfer',
      'return': 'Return',
      'countersign': 'Countersign',
      'before': 'Before',
      'after': 'After',
      'add': 'Add in order',
      'remove': 'Remove',
      'chain': 'Current countersign chain',
      'cancel': 'Cancel countersign',
      'remind': 'Remind',
      'unsaved':
          'Refresh to discard unsaved form changes before a management action.',
      'pending': 'Pending',
      'active': 'Active',
      'completed': 'Completed',
      'cancelled': 'Cancelled'
    };
    return (_isEnglish ? en : zh)[key] ??
        (throw const FormatException('Unknown workflow management label'));
  }

  String get delegateTask => _isEnglish ? 'Delegate' : '委托代理';
  String get delegateTarget => _isEnglish ? 'Select a proxy' : '选择代理人';
  String get delegateHelp => _isEnglish
      ? 'Delegate this todo only. The seat and deadline remain unchanged; the proxy uses their own permissions.'
      : '仅委托当前待办，原席位和截止时间保持不变；代理人使用本人的业务权限办理。';
  String get recallDelegation => _isEnglish ? 'Recall delegation' : '收回代理';
  String get recallReason => _isEnglish ? 'Reason for recall' : '收回原因';
  String get recallHelp => _isEnglish
      ? 'Recall before completion. Recall is not a business decision and does not require the proxy signature.'
      : '代理办结前可收回；收回不代替业务决定，也不要求代理人签名。';
  String get handoffUnsaved => _isEnglish
      ? 'Handoff does not submit fields. Restore unsaved changes first.'
      : '交接不提交字段，请先恢复尚未提交的修改。';
  String get previousPage => _isEnglish ? 'Previous' : '上一页';
  String get nextPage => _isEnglish ? 'Next' : '下一页';
  String get reloadCandidates => _isEnglish ? 'Reload candidates' : '重新加载候选';
  String get delegationStatusActive => _isEnglish ? 'Active delegation' : '代理中';
  String get delegationStatusCompleted => _isEnglish ? 'Completed' : '已办结';
  String get delegationStatusRecalled => _isEnglish ? 'Recalled' : '已收回';
  String get delegationStatusReassigned => _isEnglish ? 'Reassigned' : '管理员已改派';
  String get delegationStatusCancelled => _isEnglish ? 'Closed' : '已关闭';

  /// 状态机器码由严格协议守卫限定，译文不参与授权判断。
  String delegationStatus(String key) => switch (key) {
        'active' => delegationStatusActive,
        'completed' => delegationStatusCompleted,
        'recalled' => delegationStatusRecalled,
        'reassigned' => delegationStatusReassigned,
        'cancelled' => delegationStatusCancelled,
        _ => throw const FormatException('Unknown delegation status')
      };

  String get comment => _isEnglish ? 'Comment' : '处理意见';
  String get signatureTitle => _isEnglish ? 'Action signature' : '办理签名';
  String get signatureCanvas =>
      _isEnglish ? 'Draw your signature here' : '请在此处手写签名';
  String get signatureSave => _isEnglish ? 'Save this signature' : '保存本次签名';
  String get signatureClear => _isEnglish ? 'Clear signature' : '清除签名';
  String get signatureReady =>
      _isEnglish ? 'Signature captured for this action.' : '签名已采集，将与本次办理一同提交。';
  String get signaturePending =>
      _isEnglish ? 'Signature has not been saved.' : '尚未保存签名。';
  String get signaturePermissionRequired => _isEnglish
      ? 'This node requires signature capture permission.'
      : '此节点要求签名，当前账号缺少签名采集权限。';
  String get evidenceRequired => _isEnglish
      ? 'Provide the comment and signature required by this node.'
      : '请填写节点要求的意见并采集签名。';
  String get approve => _isEnglish ? 'Approve' : '同意';
  String get completeTask => _isEnglish ? 'Complete task' : '提交完成';
  String get submitVote => _isEnglish ? 'Submit ballot' : '提交选票';
  String get privateBallot =>
      _isEnglish ? 'Your private ballot form' : '本人的独立投票表单';
  String get voteChoice => _isEnglish ? 'Your choice' : '投票选择';
  String voteChoiceLabel(String choice) => switch (choice) {
        'yes' => _isEnglish ? 'Yes' : '赞成',
        'no' => _isEnglish ? 'No' : '反对',
        'abstain' => _isEnglish ? 'Abstain' : '弃权',
        _ => invalidField,
      };
  String voteProgress(int submitted, int total) => _isEnglish
      ? '$submitted/$total candidates submitted'
      : '已提交 $submitted/$total 人';
  String get requiredField =>
      _isEnglish ? 'This field is required.' : '此项为必填项。';
  String get invalidField => _isEnglish ? 'Check this value.' : '请检查此项内容。';
  String get voteFormUnavailable => _isEnglish
      ? 'This ballot needs a form capability that is not available on this client yet.'
      : '此投票表单需要当前客户端尚未支持的控件，请使用管理端办理。';
  String taskProgress(int completed, int required, int pending) => _isEnglish
      ? 'Completed $completed/$required, pending $pending'
      : '已完成 $completed/$required 人，待处理 $pending 人';
  String get reject => _isEnglish ? 'Reject' : '驳回';
  String get submitting => _isEnglish ? 'Submitting…' : '正在提交…';
  String get completed => _isEnglish ? 'Task completed.' : '处理成功。';
  String get claimPoolsTitle => _isEnglish ? 'Available tasks' : '待领取任务';
  String get claimTask => _isEnglish ? 'Claim task' : '领取任务';
  String get releaseTask => _isEnglish ? 'Release task' : '释放任务';
  String get actionFailed => _isEnglish ? 'Task action failed.' : '审批操作失败。';
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  bool isSupported(Locale locale) => AppLocalizations.supportedLocales.any(
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
