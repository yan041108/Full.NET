// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get attachment => 'Attachment';

  @override
  String get uploadAttachment => 'Upload attachment';

  @override
  String get removeAttachment => 'Remove attachment';

  @override
  String get reuseAttachment => 'Reuse visible attachment';

  @override
  String get addRow => 'Add row';

  @override
  String get removeRow => 'Remove row';

  @override
  String get attachmentFailed =>
      'The file is unavailable or this platform cannot open it.';

  @override
  String get appTitle => 'Full.NET';

  @override
  String get loginTitle => 'Sign in to Full.NET';

  @override
  String get loginDescription => 'Access your workflow tasks and approvals.';

  @override
  String get username => 'Username';

  @override
  String get password => 'Password';

  @override
  String get signIn => 'Sign in';

  @override
  String get signInFailed =>
      'Sign-in failed. Check your account or try again later.';

  @override
  String get workflowTodosTitle => 'My tasks';

  @override
  String get workflowTodoTitle => 'Task details';

  @override
  String get refresh => 'Refresh';

  @override
  String get emptyTodos => 'There are no pending tasks.';

  @override
  String get permissionDenied => 'This account cannot read workflow tasks.';

  @override
  String get loadFailed => 'Tasks could not be loaded. Try again later.';

  @override
  String get comment => 'Comment';

  @override
  String get signatureTitle => 'Action signature';

  @override
  String get signatureCanvas => 'Draw your signature here';

  @override
  String get signatureSave => 'Save this signature';

  @override
  String get signatureClear => 'Clear signature';

  @override
  String get signatureReady => 'Signature captured for this action.';

  @override
  String get signaturePending => 'Signature has not been saved.';

  @override
  String get signaturePermissionRequired =>
      'This node requires signature capture permission.';

  @override
  String get evidenceRequired =>
      'Provide the comment and signature required by this node.';

  @override
  String get approve => 'Approve';

  @override
  String get completeTask => 'Complete task';

  @override
  String get claimPoolsTitle => 'Available tasks';

  @override
  String get claimTask => 'Claim task';

  @override
  String get releaseTask => 'Release task';

  @override
  String get submitVote => 'Submit ballot';

  @override
  String get privateBallot => 'Your private ballot form';

  @override
  String get voteChoice => 'Your choice';

  @override
  String voteChoiceLabel(String choice) {
    String _temp0 = intl.Intl.selectLogic(
      choice,
      {
        'yes': 'Yes',
        'no': 'No',
        'abstain': 'Abstain',
        'other': 'Invalid choice',
      },
    );
    return '$_temp0';
  }

  @override
  String voteProgress(int submitted, int total) {
    return '$submitted/$total candidates submitted';
  }

  @override
  String get requiredField => 'This field is required.';

  @override
  String get invalidField => 'Check this value.';

  @override
  String get voteFormUnavailable =>
      'This ballot needs a form capability that is not available on this client yet.';

  @override
  String taskProgress(int completed, int required, int pending) {
    return 'Completed $completed/$required, pending $pending';
  }

  @override
  String get reject => 'Reject';

  @override
  String get submitting => 'Submitting…';

  @override
  String get completed => 'Task completed.';

  @override
  String get actionFailed => 'Task action failed.';

  @override
  String get pendingTasks => 'Pending';

  @override
  String get delegatedTodos => 'Delegated tasks';

  @override
  String get delegateTask => 'Delegate';

  @override
  String get delegateTarget => 'Select a proxy';

  @override
  String get delegateHelp =>
      'Delegate this todo only. The seat and deadline remain unchanged; the proxy uses their own permissions.';

  @override
  String get recallDelegation => 'Recall delegation';

  @override
  String get recallReason => 'Reason for recall';

  @override
  String get recallHelp =>
      'Recall before completion. Recall is not a business decision and does not require the proxy signature.';

  @override
  String get handoffUnsaved =>
      'Handoff does not submit fields. Restore unsaved changes first.';

  @override
  String get previousPage => 'Previous';

  @override
  String get nextPage => 'Next';

  @override
  String get reloadCandidates => 'Reload candidates';

  @override
  String get delegationStatusActive => 'Active delegation';

  @override
  String get delegationStatusCompleted => 'Completed';

  @override
  String get delegationStatusRecalled => 'Recalled';

  @override
  String get delegationStatusReassigned => 'Reassigned';

  @override
  String get delegationStatusCancelled => 'Closed';

  @override
  String managementLabel(String key) {
    String _temp0 = intl.Intl.selectLogic(
      key,
      {
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
        'cancelled': 'Cancelled',
        'other': 'Unsupported action',
      },
    );
    return '$_temp0';
  }
}
