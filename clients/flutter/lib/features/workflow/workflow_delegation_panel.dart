import 'dart:convert';
import 'package:flutter/material.dart';
import '../../core/auth/identity_session.dart';
import '../../core/http/http_problem.dart';
import '../../core/idempotency_key.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_delegation_models.dart';
import 'workflow_models.dart';
import 'workflow_todo_client.dart';

/// 单待办代理操作；候选和回执均绑定当前身份与资源，历史收回不冒充业务签署。
class WorkflowDelegationPanel extends StatefulWidget {
  const WorkflowDelegationPanel(
      {super.key,
      required this.detail,
      required this.client,
      required this.session,
      required this.comment,
      required this.busy,
      required this.unsaved,
      required this.onBusy,
      required this.onCompleted,
      required this.onRefresh,
      this.signatureFileId});
  final WorkflowTodoDetail detail;
  final WorkflowTodoClient client;
  final IdentitySession session;
  final String comment;
  final String? signatureFileId;
  final bool busy;
  final bool unsaved;
  final ValueChanged<bool> onBusy;
  final VoidCallback onCompleted;
  final Future<void> Function() onRefresh;
  @override
  State<WorkflowDelegationPanel> createState() =>
      _WorkflowDelegationPanelState();
}

class _WorkflowDelegationPanelState extends State<WorkflowDelegationPanel> {
  WorkflowTodoDelegation? _grant;
  List<WorkflowDelegateCandidate> _candidates = [];
  WorkflowDelegateCandidate? _selected;
  var _page = 1;
  var _total = 0;
  var _epoch = 0;
  var _candidateBusy = false;
  var _working = false;
  var _initialized = false;
  var _candidateFailed = false;
  Object? _identity;
  String? _feedback;
  String? _semantic;
  String? _key;
  final _recallReason = TextEditingController();
  bool get _sameIdentity => identical(_identity, widget.session.currentUser);
  bool _current(int epoch) => mounted && epoch == _epoch && _sameIdentity;
  bool get _canDelegate =>
      _sameIdentity &&
      widget.detail.statusKey == 'active' &&
      widget.detail.allowedActions.contains('delegate') &&
      widget.session.can('workflow.todos.delegate');
  bool get _canRecall =>
      _sameIdentity &&
      _grant?.canRecall == true &&
      _grant?.originTodoId == widget.detail.id &&
      widget.session.can('workflow.todos.recall_delegation');

  @override
  void initState() {
    super.initState();
    _identity = widget.session.currentUser;
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_initialized) {
      _initialized = true;
      _loadContext();
    }
  }

  @override
  void dispose() {
    _epoch++;
    _recallReason.dispose();
    super.dispose();
  }

  Future<void> _loadContext() async {
    final epoch = ++_epoch;
    try {
      final value = await widget.client.getDelegation(widget.detail.id);
      if (!_current(epoch)) return;
      setState(() => _grant = value);
      if (_canDelegate) await _loadCandidates(1);
    } catch (_) {
      if (_current(epoch)) {
        setState(() => _feedback = AppLocalizations.of(context).loadFailed);
      }
    }
  }

  Future<void> _loadCandidates(int page) async {
    if (!_canDelegate || _candidateBusy || _working || widget.busy) return;
    final epoch = _epoch;
    setState(() {
      _candidateBusy = true;
      _candidateFailed = false;
    });
    try {
      final value = await widget.client
          .listDelegationCandidates(widget.detail.id, page: page);
      if (!_current(epoch)) return;
      if (value.page != page) {
        throw const FormatException('Unexpected delegation page');
      }
      setState(() {
        _candidates = value.items
            .where((item) => item.id != widget.session.currentUser?.id)
            .toList();
        _page = value.page;
        _total = value.total;
      });
    } catch (_) {
      if (_current(epoch)) {
        setState(() {
          _candidateFailed = true;
          _candidates = [];
        });
      }
    } finally {
      if (_current(epoch)) setState(() => _candidateBusy = false);
    }
  }

  /// 只在语义不变的暂时失败后复用幂等键；冲突必须重新读取服务端的代理与表单修订。
  Future<void> _execute(bool recall) async {
    final detail = widget.detail;
    final reason = (recall ? _recallReason.text : widget.comment).trim();
    if (_working ||
        widget.busy ||
        !_sameIdentity ||
        (recall
            ? !_canRecall
            : !_canDelegate || widget.unsaved || _selected == null)) {
      return;
    }
    final l10n = AppLocalizations.of(context);
    if (detail.commentRequired && reason.isEmpty ||
        !recall && detail.signRequired && widget.signatureFileId == null) {
      setState(() => _feedback = l10n.evidenceRequired);
      return;
    }
    final epoch = _epoch;
    final revision = recall ? _grant!.proxyRevision : detail.revision;
    final formRevision =
        recall ? _grant!.formRevision : detail.submissionRevision;
    final semantic = jsonEncode([
      detail.id,
      recall,
      revision,
      formRevision,
      recall ? null : _selected!.id,
      reason,
      recall ? null : widget.signatureFileId
    ]);
    if (_semantic != semantic) {
      _semantic = semantic;
      _key = createIdempotencyKey();
    }
    setState(() {
      _working = true;
      _feedback = null;
    });
    widget.onBusy(true);
    try {
      if (recall) {
        await widget.client.recallDelegation(detail.id,
            revision: revision,
            formRevision: formRevision,
            key: _key!,
            comment: reason.isEmpty ? null : reason);
      } else {
        await widget.client.delegate(detail.id, _selected!.id,
            revision: revision,
            formRevision: formRevision,
            key: _key!,
            comment: reason.isEmpty ? null : reason,
            signatureFileId: widget.signatureFileId);
      }
      if (!_current(epoch)) return;
      _key = null;
      _semantic = null;
      widget.onCompleted();
    } catch (error) {
      if (!_current(epoch)) return;
      if (error is HttpProblem &&
          !{408, 429}.contains(error.status) &&
          error.status < 500) {
        _key = null;
        _semantic = null;
      }
      setState(() => _feedback = l10n.actionFailed);
      if (error is HttpProblem && error.status == 409) await widget.onRefresh();
    } finally {
      if (_current(epoch)) {
        setState(() => _working = false);
        widget.onBusy(false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!_sameIdentity) return const SizedBox.shrink();
    final l10n = AppLocalizations.of(context);
    final options = <WorkflowDelegateCandidate>[
      if (_selected != null &&
          !_candidates.any((item) => item.id == _selected!.id))
        _selected!,
      ..._candidates
    ];
    final ready = !_working &&
        !widget.busy &&
        !widget.unsaved &&
        _selected != null &&
        (!widget.detail.commentRequired || widget.comment.trim().isNotEmpty) &&
        (!widget.detail.signRequired || widget.signatureFileId != null);
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      if (_canDelegate) ...[
        Text(l10n.delegateHelp),
        DropdownButtonFormField<String>(
            key: ValueKey('${widget.detail.id}:$_page:${_selected?.id}'),
            initialValue: _selected?.id,
            decoration: InputDecoration(labelText: l10n.delegateTarget),
            items: options
                .map((item) =>
                    DropdownMenuItem(value: item.id, child: Text(item.label)))
                .toList(),
            onChanged: _working || widget.busy || _candidateBusy
                ? null
                : (id) => setState(() =>
                    _selected = options.firstWhere((item) => item.id == id))),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          TextButton(
              onPressed: _page <= 1 || _candidateBusy || _working || widget.busy
                  ? null
                  : () => _loadCandidates(_page - 1),
              child: Text(l10n.previousPage)),
          Text('$_page / ${(_total / 20).ceil().clamp(1, 2147483647)}'),
          TextButton(
              onPressed: _page * 20 >= _total ||
                      _candidateBusy ||
                      _working ||
                      widget.busy
                  ? null
                  : () => _loadCandidates(_page + 1),
              child: Text(l10n.nextPage))
        ]),
        if (_candidateFailed)
          TextButton(
              onPressed:
                  _working || widget.busy ? null : () => _loadCandidates(_page),
              child: Text(l10n.reloadCandidates)),
        if (widget.unsaved) Text(l10n.handoffUnsaved),
        OutlinedButton(
            key: const ValueKey('workflow-delegate'),
            onPressed: ready ? () => _execute(false) : null,
            child: Text(l10n.delegateTask)),
      ],
      if (_grant case final grant?) ...[
        Text(l10n.delegationStatus(grant.status)),
        Text('${grant.originalAssigneeUserId} → ${grant.proxyUserId}'),
        if (_canRecall) ...[
          Text(l10n.recallHelp),
          TextField(
              controller: _recallReason,
              maxLength: 512,
              enabled: !_working && !widget.busy,
              decoration: InputDecoration(labelText: l10n.recallReason),
              onChanged: (_) => setState(() {})),
          OutlinedButton(
              key: const ValueKey('workflow-recall-delegation'),
              onPressed: _working ||
                      widget.busy ||
                      widget.detail.commentRequired &&
                          _recallReason.text.trim().isEmpty
                  ? null
                  : () => _execute(true),
              child: Text(l10n.recallDelegation)),
        ]
      ],
      if (_feedback != null)
        Text(_feedback!,
            style: TextStyle(color: Theme.of(context).colorScheme.error)),
    ]);
  }
}
