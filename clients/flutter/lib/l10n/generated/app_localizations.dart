import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_zh.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'generated/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
      : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
    delegate,
    GlobalMaterialLocalizations.delegate,
    GlobalCupertinoLocalizations.delegate,
    GlobalWidgetsLocalizations.delegate,
  ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('zh')
  ];

  /// No description provided for @attachment.
  ///
  /// In en, this message translates to:
  /// **'Attachment'**
  String get attachment;

  /// No description provided for @uploadAttachment.
  ///
  /// In en, this message translates to:
  /// **'Upload attachment'**
  String get uploadAttachment;

  /// No description provided for @removeAttachment.
  ///
  /// In en, this message translates to:
  /// **'Remove attachment'**
  String get removeAttachment;

  /// No description provided for @reuseAttachment.
  ///
  /// In en, this message translates to:
  /// **'Reuse visible attachment'**
  String get reuseAttachment;

  /// No description provided for @addRow.
  ///
  /// In en, this message translates to:
  /// **'Add row'**
  String get addRow;

  /// No description provided for @removeRow.
  ///
  /// In en, this message translates to:
  /// **'Remove row'**
  String get removeRow;

  /// No description provided for @attachmentFailed.
  ///
  /// In en, this message translates to:
  /// **'The file is unavailable or this platform cannot open it.'**
  String get attachmentFailed;

  /// No description provided for @appTitle.
  ///
  /// In en, this message translates to:
  /// **'Full.NET'**
  String get appTitle;

  /// No description provided for @loginTitle.
  ///
  /// In en, this message translates to:
  /// **'Sign in to Full.NET'**
  String get loginTitle;

  /// No description provided for @loginDescription.
  ///
  /// In en, this message translates to:
  /// **'Access your workflow tasks and approvals.'**
  String get loginDescription;

  /// No description provided for @username.
  ///
  /// In en, this message translates to:
  /// **'Username'**
  String get username;

  /// No description provided for @password.
  ///
  /// In en, this message translates to:
  /// **'Password'**
  String get password;

  /// No description provided for @signIn.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get signIn;

  /// No description provided for @signInFailed.
  ///
  /// In en, this message translates to:
  /// **'Sign-in failed. Check your account or try again later.'**
  String get signInFailed;

  /// No description provided for @workflowTodosTitle.
  ///
  /// In en, this message translates to:
  /// **'My tasks'**
  String get workflowTodosTitle;

  /// No description provided for @workflowTodoTitle.
  ///
  /// In en, this message translates to:
  /// **'Task details'**
  String get workflowTodoTitle;

  /// No description provided for @refresh.
  ///
  /// In en, this message translates to:
  /// **'Refresh'**
  String get refresh;

  /// No description provided for @emptyTodos.
  ///
  /// In en, this message translates to:
  /// **'There are no pending tasks.'**
  String get emptyTodos;

  /// No description provided for @permissionDenied.
  ///
  /// In en, this message translates to:
  /// **'This account cannot read workflow tasks.'**
  String get permissionDenied;

  /// No description provided for @loadFailed.
  ///
  /// In en, this message translates to:
  /// **'Tasks could not be loaded. Try again later.'**
  String get loadFailed;

  /// No description provided for @comment.
  ///
  /// In en, this message translates to:
  /// **'Comment'**
  String get comment;

  /// No description provided for @signatureTitle.
  ///
  /// In en, this message translates to:
  /// **'Action signature'**
  String get signatureTitle;

  /// No description provided for @signatureCanvas.
  ///
  /// In en, this message translates to:
  /// **'Draw your signature here'**
  String get signatureCanvas;

  /// No description provided for @signatureSave.
  ///
  /// In en, this message translates to:
  /// **'Save this signature'**
  String get signatureSave;

  /// No description provided for @signatureClear.
  ///
  /// In en, this message translates to:
  /// **'Clear signature'**
  String get signatureClear;

  /// No description provided for @signatureReady.
  ///
  /// In en, this message translates to:
  /// **'Signature captured for this action.'**
  String get signatureReady;

  /// No description provided for @signaturePending.
  ///
  /// In en, this message translates to:
  /// **'Signature has not been saved.'**
  String get signaturePending;

  /// No description provided for @signaturePermissionRequired.
  ///
  /// In en, this message translates to:
  /// **'This node requires signature capture permission.'**
  String get signaturePermissionRequired;

  /// No description provided for @evidenceRequired.
  ///
  /// In en, this message translates to:
  /// **'Provide the comment and signature required by this node.'**
  String get evidenceRequired;

  /// No description provided for @approve.
  ///
  /// In en, this message translates to:
  /// **'Approve'**
  String get approve;

  /// No description provided for @completeTask.
  ///
  /// In en, this message translates to:
  /// **'Complete task'**
  String get completeTask;

  /// No description provided for @claimPoolsTitle.
  ///
  /// In en, this message translates to:
  /// **'Available tasks'**
  String get claimPoolsTitle;

  /// No description provided for @claimTask.
  ///
  /// In en, this message translates to:
  /// **'Claim task'**
  String get claimTask;

  /// No description provided for @releaseTask.
  ///
  /// In en, this message translates to:
  /// **'Release task'**
  String get releaseTask;

  /// No description provided for @submitVote.
  ///
  /// In en, this message translates to:
  /// **'Submit ballot'**
  String get submitVote;

  /// No description provided for @privateBallot.
  ///
  /// In en, this message translates to:
  /// **'Your private ballot form'**
  String get privateBallot;

  /// No description provided for @voteChoice.
  ///
  /// In en, this message translates to:
  /// **'Your choice'**
  String get voteChoice;

  /// No description provided for @voteChoiceLabel.
  ///
  /// In en, this message translates to:
  /// **'{choice, select, yes{Yes} no{No} abstain{Abstain} other{Invalid choice}}'**
  String voteChoiceLabel(String choice);

  /// No description provided for @voteProgress.
  ///
  /// In en, this message translates to:
  /// **'{submitted}/{total} candidates submitted'**
  String voteProgress(int submitted, int total);

  /// No description provided for @requiredField.
  ///
  /// In en, this message translates to:
  /// **'This field is required.'**
  String get requiredField;

  /// No description provided for @invalidField.
  ///
  /// In en, this message translates to:
  /// **'Check this value.'**
  String get invalidField;

  /// No description provided for @voteFormUnavailable.
  ///
  /// In en, this message translates to:
  /// **'This ballot needs a form capability that is not available on this client yet.'**
  String get voteFormUnavailable;

  /// No description provided for @taskProgress.
  ///
  /// In en, this message translates to:
  /// **'Completed {completed}/{required}, pending {pending}'**
  String taskProgress(int completed, int required, int pending);

  /// No description provided for @reject.
  ///
  /// In en, this message translates to:
  /// **'Reject'**
  String get reject;

  /// No description provided for @submitting.
  ///
  /// In en, this message translates to:
  /// **'Submitting…'**
  String get submitting;

  /// No description provided for @completed.
  ///
  /// In en, this message translates to:
  /// **'Task completed.'**
  String get completed;

  /// No description provided for @actionFailed.
  ///
  /// In en, this message translates to:
  /// **'Task action failed.'**
  String get actionFailed;

  /// No description provided for @pendingTasks.
  ///
  /// In en, this message translates to:
  /// **'Pending'**
  String get pendingTasks;

  /// No description provided for @delegatedTodos.
  ///
  /// In en, this message translates to:
  /// **'Delegated tasks'**
  String get delegatedTodos;

  /// No description provided for @delegateTask.
  ///
  /// In en, this message translates to:
  /// **'Delegate'**
  String get delegateTask;

  /// No description provided for @delegateTarget.
  ///
  /// In en, this message translates to:
  /// **'Select a proxy'**
  String get delegateTarget;

  /// No description provided for @delegateHelp.
  ///
  /// In en, this message translates to:
  /// **'Delegate this todo only. The seat and deadline remain unchanged; the proxy uses their own permissions.'**
  String get delegateHelp;

  /// No description provided for @recallDelegation.
  ///
  /// In en, this message translates to:
  /// **'Recall delegation'**
  String get recallDelegation;

  /// No description provided for @recallReason.
  ///
  /// In en, this message translates to:
  /// **'Reason for recall'**
  String get recallReason;

  /// No description provided for @recallHelp.
  ///
  /// In en, this message translates to:
  /// **'Recall before completion. Recall is not a business decision and does not require the proxy signature.'**
  String get recallHelp;

  /// No description provided for @handoffUnsaved.
  ///
  /// In en, this message translates to:
  /// **'Handoff does not submit fields. Restore unsaved changes first.'**
  String get handoffUnsaved;

  /// No description provided for @previousPage.
  ///
  /// In en, this message translates to:
  /// **'Previous'**
  String get previousPage;

  /// No description provided for @nextPage.
  ///
  /// In en, this message translates to:
  /// **'Next'**
  String get nextPage;

  /// No description provided for @reloadCandidates.
  ///
  /// In en, this message translates to:
  /// **'Reload candidates'**
  String get reloadCandidates;

  /// No description provided for @delegationStatusActive.
  ///
  /// In en, this message translates to:
  /// **'Active delegation'**
  String get delegationStatusActive;

  /// No description provided for @delegationStatusCompleted.
  ///
  /// In en, this message translates to:
  /// **'Completed'**
  String get delegationStatusCompleted;

  /// No description provided for @delegationStatusRecalled.
  ///
  /// In en, this message translates to:
  /// **'Recalled'**
  String get delegationStatusRecalled;

  /// No description provided for @delegationStatusReassigned.
  ///
  /// In en, this message translates to:
  /// **'Reassigned'**
  String get delegationStatusReassigned;

  /// No description provided for @delegationStatusCancelled.
  ///
  /// In en, this message translates to:
  /// **'Closed'**
  String get delegationStatusCancelled;

  /// No description provided for @managementLabel.
  ///
  /// In en, this message translates to:
  /// **'{key, select, history{Completed history} choose{Choose a target} transfer{Transfer} return{Return} countersign{Countersign} before{Before} after{After} add{Add in order} remove{Remove} chain{Current countersign chain} cancel{Cancel countersign} remind{Remind} unsaved{Refresh to discard unsaved form changes before a management action.} pending{Pending} active{Active} completed{Completed} cancelled{Cancelled} other{Unsupported action}}'**
  String managementLabel(String key);
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'zh'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'zh':
      return AppLocalizationsZh();
  }

  throw FlutterError(
      'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
      'an issue with the localizations generation tool. Please file an issue '
      'on GitHub with a reproducible sample app and the gen-l10n configuration '
      'that was used.');
}
