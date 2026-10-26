import 'dart:typed_data';
import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/identity_session.dart';
import '../../core/idempotency_key.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_models.dart';
import 'workflow_todo_client.dart';
import 'workflow_signature_capture.dart';
import 'workflow_vote_form_editor.dart';
import 'workflow_delegation_panel.dart';
import 'workflow_human_management_panel.dart';

class WorkflowTodoDetailPage extends StatefulWidget {
  const WorkflowTodoDetailPage({
    super.key,
    required this.todoId,
    required this.session,
    required this.client,
  });

  final String todoId;
  final IdentitySession session;
  final WorkflowTodoClient client;

  @override
  State<WorkflowTodoDetailPage> createState() => _WorkflowTodoDetailPageState();
}

class _WorkflowTodoDetailPageState extends State<WorkflowTodoDetailPage> {
  var _loading = true;
  var _submitting = false;
  String? _feedback;
  WorkflowTodoDetail? _detail;
  final _commentController = TextEditingController();
  String? _idempotencyKey;
  String? _pendingSemantic;
  String? _signatureFileId;
  var _signatureUploading = false;
  var _initialized = false;
  final _voteFormKey = GlobalKey<FormState>();
  Map<String, dynamic> _voteValues = {};
  String? _voteChoice;
  int _identityEpoch = 0;

  @override
  void initState() {
    super.initState();
    widget.session.addListener(_identityChanged);
  }

  /// 会话或资源改变立即撤下旧投影；旧读写回执不能更新新身份页面。
  void _identityChanged() {
    _identityEpoch++;
    if (!mounted) return;
    setState(() {
      _detail = null;
      _signatureFileId = null;
      _commentController.clear();
      _idempotencyKey = null;
      _pendingSemantic = null;
      _voteValues = {};
      _voteChoice = null;
      _submitting = false;
      _signatureUploading = false;
      _loading = false;
      _feedback = null;
    });
    if (_initialized && widget.session.can('workflow.todos.read')) _load();
  }

  @override
  void didUpdateWidget(covariant WorkflowTodoDetailPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.session != widget.session) {
      oldWidget.session.removeListener(_identityChanged);
      widget.session.addListener(_identityChanged);
    }
    if (oldWidget.session != widget.session ||
        oldWidget.todoId != widget.todoId ||
        oldWidget.client != widget.client) {
      _identityChanged();
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // 本地化依赖在此时才可订阅，后续依赖变化不能重复丢弃用户已填写的内容。
    if (!_initialized) {
      _initialized = true;
      _load();
    }
  }

  @override
  void dispose() {
    _identityEpoch++;
    widget.session.removeListener(_identityChanged);
    _commentController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final l10n = AppLocalizations.of(context);
    final epoch = ++_identityEpoch;
    setState(() {
      _loading = true;
      _feedback = null;
      _detail = null;
      _submitting = false;
      _signatureUploading = false;
    });
    try {
      if (!widget.session.can('workflow.todos.read')) {
        throw StateError('permission-denied');
      }
      final detail = await widget.client.getRuntime(widget.todoId);
      if (!mounted || epoch != _identityEpoch) return;
      setState(() {
        _detail = detail;
        _idempotencyKey = null;
        _pendingSemantic = null;
        _signatureFileId = null;
        _commentController.clear();
        _voteValues =
            Map<String, dynamic>.from(detail.voteForm?.values ?? const {});
        _voteChoice = detail.voteForm?.choiceKey;
      });
    } catch (_) {
      if (mounted && epoch == _identityEpoch) {
        setState(() => _feedback = l10n.loadFailed);
      }
    } finally {
      if (mounted && epoch == _identityEpoch) setState(() => _loading = false);
    }
  }

  Future<void> _act(String action) async {
    final actionEpoch = _identityEpoch;
    final l10n = AppLocalizations.of(context);
    final detail = _detail;
    if (detail == null ||
        _submitting ||
        _signatureUploading ||
        detail.statusKey != 'active' ||
        !detail.allowedActions.contains(action)) {
      return;
    }
    final permission = switch (action) {
      'approve' => 'workflow.todos.approve',
      'reject' => 'workflow.todos.reject',
      'complete' => 'workflow.todos.complete',
      'release' => 'workflow.todos.release',
      _ => null,
    };
    if (permission == null || !widget.session.can(permission)) {
      setState(() => _feedback = l10n.permissionDenied);
      return;
    }

    if (action == 'release' &&
        (detail.activityKindKey != 'human.claim' ||
            detail.claimProgress?.allowRelease != true)) {
      return;
    }
    if ((detail.commentRequired && _commentController.text.trim().isEmpty) ||
        (detail.signRequired && _signatureFileId == null)) {
      setState(() => _feedback = l10n.evidenceRequired);
      return;
    }
    final semantic = jsonEncode([
      action,
      detail.id,
      detail.revision,
      detail.submissionRevision,
      detail.claimProgress?.revision,
      _commentController.text.trim(),
      _signatureFileId
    ]);
    if (_pendingSemantic != semantic) {
      _idempotencyKey = createIdempotencyKey();
      _pendingSemantic = semantic;
    }
    final request = ActWorkflowTodoRequest(
      expectedRevision: detail.revision,
      expectedFormRevision: detail.submissionRevision,
      fieldPatch: const {},
      comment: _commentController.text.trim().isEmpty
          ? null
          : _commentController.text.trim(),
      idempotencyKey: _idempotencyKey!,
      signatureFileId: _signatureFileId,
    );

    setState(() {
      _submitting = true;
      _feedback = null;
    });
    try {
      if (action == 'release') {
        await widget.client.release(widget.todoId,
            revision: detail.revision,
            poolRevision: detail.claimProgress!.revision,
            formRevision: detail.submissionRevision,
            key: _idempotencyKey!,
            comment: request.comment,
            signatureFileId: _signatureFileId);
      } else if (action == 'approve') {
        await widget.client.approve(widget.todoId, request);
      } else if (action == 'complete') {
        await widget.client.complete(widget.todoId, request);
      } else {
        await widget.client.reject(widget.todoId, request);
      }
      if (!mounted || actionEpoch != _identityEpoch) {
        return;
      }
      Navigator.of(context).pop();
    } catch (_) {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _feedback = l10n.actionFailed);
      }
    } finally {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _submitting = false);
      }
    }
  }

  Future<void> _castVote() async {
    final actionEpoch = _identityEpoch;
    final detail = _detail;
    final form = detail?.voteForm;
    final choice = _voteChoice;
    final l10n = AppLocalizations.of(context);
    if (detail == null ||
        form == null ||
        choice == null ||
        form.readOnly ||
        !WorkflowVoteFormEditor.supports(form) ||
        detail.statusKey != 'active' ||
        !detail.allowedActions.contains('vote') ||
        !widget.session.can('workflow.todos.vote') ||
        _submitting ||
        _signatureUploading ||
        !(_voteFormKey.currentState?.validate() ?? false)) {
      return;
    }
    if ((detail.commentRequired && _commentController.text.trim().isEmpty) ||
        (detail.signRequired && _signatureFileId == null)) {
      setState(() => _feedback = l10n.evidenceRequired);
      return;
    }
    final semantic = jsonEncode([
      'vote',
      detail.id,
      detail.revision,
      form.revision,
      choice,
      _voteValues,
      _commentController.text.trim(),
      _signatureFileId
    ]);
    if (_pendingSemantic != semantic) {
      _pendingSemantic = semantic;
      _idempotencyKey = createIdempotencyKey();
    }
    final request = VoteWorkflowTodoRequest(
        expectedRevision: detail.revision,
        expectedBallotRevision: form.revision,
        choiceKey: choice,
        values: Map<String, dynamic>.from(_voteValues),
        idempotencyKey: _idempotencyKey!,
        comment: _commentController.text.trim().isEmpty
            ? null
            : _commentController.text.trim(),
        signatureFileId: _signatureFileId);
    setState(() {
      _submitting = true;
      _feedback = null;
    });
    try {
      await widget.client.vote(detail.id, request);
      if (mounted && actionEpoch == _identityEpoch) {
        Navigator.of(context).pop();
      }
    } catch (_) {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _feedback = l10n.actionFailed);
      }
    } finally {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _submitting = false);
      }
    }
  }

  Future<void> _captureSignature(Uint8List bytes) async {
    final actionEpoch = _identityEpoch;
    final detail = _detail;
    if (detail == null ||
        _submitting ||
        _signatureUploading ||
        !widget.session.can('workflow.todos.sign')) {
      return;
    }
    setState(() {
      _signatureUploading = true;
      _signatureFileId = null;
    });
    try {
      final fileId = await widget.client
          .uploadSignature(detail.id, detail.revision, bytes);
      if (mounted &&
          actionEpoch == _identityEpoch &&
          _detail?.id == detail.id) {
        setState(() => _signatureFileId = fileId);
      }
    } catch (_) {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _feedback = AppLocalizations.of(context).actionFailed);
      }
    } finally {
      if (mounted && actionEpoch == _identityEpoch) {
        setState(() => _signatureUploading = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final detail = _detail;
    final canApprove = detail != null &&
        detail.statusKey == 'active' &&
        detail.allowedActions.contains('approve') &&
        widget.session.can('workflow.todos.approve');
    final canReject = detail != null &&
        detail.statusKey == 'active' &&
        detail.allowedActions.contains('reject') &&
        widget.session.can('workflow.todos.reject');
    final canComplete = detail != null &&
        detail.statusKey == 'active' &&
        {'human.task', 'human.claim'}.contains(detail.activityKindKey) &&
        detail.allowedActions.contains('complete') &&
        widget.session.can('workflow.todos.complete');
    final canVote = detail != null &&
        detail.statusKey == 'active' &&
        detail.activityKindKey == 'human.vote' &&
        detail.allowedActions.contains('vote') &&
        detail.voteForm?.readOnly == false &&
        widget.session.can('workflow.todos.vote');
    final unsavedVote = detail?.voteForm != null &&
        (_voteChoice != detail!.voteForm!.choiceKey ||
            jsonEncode(_voteValues) != jsonEncode(detail.voteForm!.values));

    return Scaffold(
      appBar: AppBar(title: Text(l10n.workflowTodoTitle), actions: [
        if (widget.session.can('workflow.todos.read'))
          IconButton(
              key: const ValueKey('workflow-detail-refresh'),
              tooltip: l10n.refresh,
              onPressed:
                  _loading || _submitting || _signatureUploading ? null : _load,
              icon: const Icon(Icons.refresh))
      ]),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : detail == null
              ? Center(child: Text(_feedback ?? l10n.loadFailed))
              : Padding(
                  padding: const EdgeInsets.all(16),
                  child: ListView(
                    children: [
                      Text('${detail.statusKey} · R${detail.revision}'),
                      if (detail.taskProgress case final progress?)
                        Text(l10n.taskProgress(
                            progress.completedCount,
                            progress.requiredCompletionCount,
                            progress.pendingCount)),
                      const SizedBox(height: 16),
                      Text(detail.submission.toString()),
                      if (detail.voteForm case final form?) ...[
                        const SizedBox(height: 16),
                        Text(l10n.privateBallot,
                            style: Theme.of(context).textTheme.titleMedium),
                        if (detail.voteProgress case final progress?)
                          Text(l10n.voteProgress(
                              progress.submittedCount, progress.memberCount)),
                        WorkflowVoteFormEditor(
                            key: ValueKey('${detail.id}:ballot'),
                            form: form,
                            formKey: _voteFormKey,
                            enabled: canVote && !_submitting,
                            onChanged: (values) =>
                                setState(() => _voteValues = values)),
                        DropdownButtonFormField<String>(
                            key: ValueKey('${detail.id}:choice'),
                            initialValue: _voteChoice,
                            decoration:
                                InputDecoration(labelText: l10n.voteChoice),
                            items: [
                              for (final choice in [
                                'yes',
                                'no',
                                if (form.allowAbstain) 'abstain'
                              ])
                                DropdownMenuItem(
                                    value: choice,
                                    child: Text(l10n.voteChoiceLabel(choice)))
                            ],
                            onChanged: canVote && !_submitting
                                ? (value) => setState(() => _voteChoice = value)
                                : null),
                      ],
                      const SizedBox(height: 16),
                      if (detail.statusKey == 'active')
                        TextField(
                          controller: _commentController,
                          decoration: InputDecoration(labelText: l10n.comment),
                          maxLines: 3,
                          enabled: !_submitting,
                          onChanged: (_) => setState(() {}),
                        ),
                      if (detail.statusKey == 'active' &&
                          widget.session.can('workflow.todos.sign'))
                        WorkflowSignatureCapture(
                          key: ValueKey(detail.id),
                          busy: _submitting || _signatureUploading,
                          captured: _signatureFileId != null,
                          onCapture: _captureSignature,
                          onClear: () =>
                              setState(() => _signatureFileId = null),
                        )
                      else if (detail.statusKey == 'active' &&
                          detail.signRequired)
                        Text(l10n.signaturePermissionRequired),
                      if (widget.session.can('workflow.todos.delegate') ||
                          widget.session
                              .can('workflow.todos.recall_delegation'))
                        WorkflowDelegationPanel(
                            key: ValueKey(
                                '${detail.id}:${detail.revision}:$_identityEpoch'),
                            detail: detail,
                            client: widget.client,
                            session: widget.session,
                            comment: _commentController.text,
                            signatureFileId: _signatureFileId,
                            busy: _submitting || _signatureUploading,
                            unsaved: unsavedVote,
                            onBusy: (busy) {
                              if (mounted) setState(() => _submitting = busy);
                            },
                            onCompleted: () {
                              if (mounted) Navigator.of(context).pop();
                            },
                            onRefresh: _load),
                      WorkflowHumanManagementPanel(
                          key: ValueKey(
                              'management:${detail.id}:${detail.revision}:$_identityEpoch'),
                          detail: detail,
                          client: widget.client,
                          session: widget.session,
                          comment: _commentController.text,
                          signatureFileId: _signatureFileId,
                          busy: _submitting || _signatureUploading,
                          unsaved: unsavedVote,
                          onBusy: (busy) {
                            if (mounted) setState(() => _submitting = busy);
                          },
                          onCompleted: () {
                            if (mounted) Navigator.of(context).pop();
                          },
                          onRefresh: _load),
                      if (_feedback != null) ...[
                        const SizedBox(height: 12),
                        Text(
                          _feedback!,
                          style: TextStyle(
                              color: Theme.of(context).colorScheme.error),
                        ),
                      ],
                      const SizedBox(height: 16),
                      if (canReject)
                        OutlinedButton(
                          onPressed: _submitting || _signatureUploading
                              ? null
                              : () => _act('reject'),
                          child: Text(l10n.reject),
                        ),
                      if (canApprove) ...[
                        const SizedBox(height: 8),
                        FilledButton(
                          onPressed: _submitting || _signatureUploading
                              ? null
                              : () => _act('approve'),
                          child: Text(
                              _submitting ? l10n.submitting : l10n.approve),
                        ),
                      ],
                      if (canComplete) ...[
                        const SizedBox(height: 8),
                        FilledButton(
                          onPressed: _submitting || _signatureUploading
                              ? null
                              : () => _act('complete'),
                          child: Text(_submitting
                              ? l10n.submitting
                              : l10n.completeTask),
                        ),
                      ],
                      if (detail.activityKindKey == 'human.claim' &&
                          detail.claimProgress?.allowRelease == true &&
                          detail.statusKey == 'active' &&
                          detail.allowedActions.contains('release') &&
                          widget.session.can('workflow.todos.release')) ...[
                        const SizedBox(height: 8),
                        OutlinedButton(
                            onPressed: _submitting || _signatureUploading
                                ? null
                                : () => _act('release'),
                            child: Text(l10n.releaseTask)),
                      ],
                      if (canVote) ...[
                        const SizedBox(height: 8),
                        FilledButton(
                            onPressed: _submitting ||
                                    _signatureUploading ||
                                    _voteChoice == null ||
                                    !WorkflowVoteFormEditor.supports(
                                        detail.voteForm!)
                                ? null
                                : _castVote,
                            child: Text(_submitting
                                ? l10n.submitting
                                : l10n.submitVote)),
                      ],
                    ],
                  ),
                ),
    );
  }
}
