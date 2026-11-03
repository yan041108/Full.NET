// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Chinese (`zh`).
class AppLocalizationsZh extends AppLocalizations {
  AppLocalizationsZh([String locale = 'zh']) : super(locale);

  @override
  String get attachment => '附件';

  @override
  String get uploadAttachment => '上传附件';

  @override
  String get removeAttachment => '移除附件';

  @override
  String get reuseAttachment => '复用当前可见附件';

  @override
  String get addRow => '添加明细';

  @override
  String get removeRow => '移除明细';

  @override
  String get attachmentFailed => '文件不可用或当前平台无法打开此类型。';

  @override
  String get appTitle => 'Full.NET';

  @override
  String get loginTitle => '登录 Full.NET';

  @override
  String get loginDescription => '访问你的工作流待办与审批任务。';

  @override
  String get username => '用户名';

  @override
  String get password => '密码';

  @override
  String get signIn => '登录';

  @override
  String get signInFailed => '登录失败，请检查账号或稍后重试。';

  @override
  String get workflowTodosTitle => '我的待办';

  @override
  String get workflowTodoTitle => '待办详情';

  @override
  String get refresh => '刷新';

  @override
  String get emptyTodos => '当前没有待办任务。';

  @override
  String get permissionDenied => '当前账号没有查看待办的权限。';

  @override
  String get loadFailed => '待办加载失败，请稍后重试。';

  @override
  String get comment => '处理意见';

  @override
  String get signatureTitle => '办理签名';

  @override
  String get signatureCanvas => '请在此处手写签名';

  @override
  String get signatureSave => '保存本次签名';

  @override
  String get signatureClear => '清除签名';

  @override
  String get signatureReady => '签名已采集，将与本次办理一同提交。';

  @override
  String get signaturePending => '尚未保存签名。';

  @override
  String get signaturePermissionRequired => '此节点要求签名，当前账号缺少签名采集权限。';

  @override
  String get evidenceRequired => '请填写节点要求的意见并采集签名。';

  @override
  String get approve => '同意';

  @override
  String get completeTask => '提交完成';

  @override
  String get claimPoolsTitle => '待领取任务';

  @override
  String get claimTask => '领取任务';

  @override
  String get releaseTask => '释放任务';

  @override
  String get submitVote => '提交选票';

  @override
  String get privateBallot => '本人的独立投票表单';

  @override
  String get voteChoice => '投票选择';

  @override
  String voteChoiceLabel(String choice) {
    String _temp0 = intl.Intl.selectLogic(
      choice,
      {
        'yes': '赞成',
        'no': '反对',
        'abstain': '弃权',
        'other': '选择无效',
      },
    );
    return '$_temp0';
  }

  @override
  String voteProgress(int submitted, int total) {
    return '已提交 $submitted/$total 人';
  }

  @override
  String get requiredField => '此项为必填项。';

  @override
  String get invalidField => '请检查此项内容。';

  @override
  String get voteFormUnavailable => '此投票表单需要当前客户端尚未支持的控件，请使用管理端办理。';

  @override
  String taskProgress(int completed, int required, int pending) {
    return '已完成 $completed/$required 人，待处理 $pending 人';
  }

  @override
  String get reject => '驳回';

  @override
  String get submitting => '正在提交…';

  @override
  String get completed => '处理成功。';

  @override
  String get actionFailed => '审批操作失败。';

  @override
  String get pendingTasks => '待办';

  @override
  String get delegatedTodos => '我委托的待办';

  @override
  String get delegateTask => '委托代理';

  @override
  String get delegateTarget => '选择代理人';

  @override
  String get delegateHelp => '仅委托当前待办，原席位和截止时间保持不变；代理人使用本人的业务权限办理。';

  @override
  String get recallDelegation => '收回代理';

  @override
  String get recallReason => '收回原因';

  @override
  String get recallHelp => '代理办结前可收回；收回不代替业务决定，也不要求代理人签名。';

  @override
  String get handoffUnsaved => '交接不提交字段，请先恢复尚未提交的修改。';

  @override
  String get previousPage => '上一页';

  @override
  String get nextPage => '下一页';

  @override
  String get reloadCandidates => '重新加载候选';

  @override
  String get delegationStatusActive => '代理中';

  @override
  String get delegationStatusCompleted => '已办结';

  @override
  String get delegationStatusRecalled => '已收回';

  @override
  String get delegationStatusReassigned => '管理员已改派';

  @override
  String get delegationStatusCancelled => '已关闭';

  @override
  String managementLabel(String key) {
    String _temp0 = intl.Intl.selectLogic(
      key,
      {
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
        'cancelled': '已取消',
        'other': 'Unsupported action',
      },
    );
    return '$_temp0';
  }
}
