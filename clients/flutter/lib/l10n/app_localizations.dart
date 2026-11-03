import 'generated/app_localizations.dart';
export 'generated/app_localizations.dart';

/// 状态机器码由严格协议守卫限定；生成的译文不参与授权判断。
extension WorkflowStatusLocalizations on AppLocalizations {
  String delegationStatus(String key) => switch (key) {
        'active' => delegationStatusActive,
        'completed' => delegationStatusCompleted,
        'recalled' => delegationStatusRecalled,
        'reassigned' => delegationStatusReassigned,
        'cancelled' => delegationStatusCancelled,
        _ => throw const FormatException('Unknown delegation status'),
      };
}
