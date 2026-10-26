import 'dart:convert';
import 'package:flutter/material.dart';
import '../../core/auth/identity_session.dart';
import '../../core/http/http_problem.dart';
import '../../core/idempotency_key.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_models.dart';
import 'workflow_todo_client.dart';
import 'workflow_human_management_models.dart';

/// 管理动作使用独立权限和服务端实际目标，身份代次隔离候选、证据与迟到回执。
class WorkflowHumanManagementPanel extends StatefulWidget {
  const WorkflowHumanManagementPanel(
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
  State<WorkflowHumanManagementPanel> createState() =>
      _WorkflowHumanManagementPanelState();
}

class _Target {
  const _Target(this.id, this.label);
  final String id;
  final String label;
}

class _WorkflowHumanManagementPanelState
    extends State<WorkflowHumanManagementPanel> {
  var _epoch = 0;
  Object? _identity;
  var _initialized = false;
  var _action = 'transfer';
  var _direction = 'before';
  var _page = 1;
  var _hasNext = false;
  var _loading = false;
  var _working = false;
  var _failed = false;
  var _chainLoaded = false;
  String? _feedback;
  String? _semantic;
  String? _key;
  List<_Target> _rows = [];
  List<_Target> _members = [];
  _Target? _selected;
  WorkflowCountersignChain? _chain;
  WorkflowReminderTargets? _reminders;
  final _reason = TextEditingController();
  bool get _sameIdentity => identical(_identity, widget.session.currentUser);
  bool _current(int epoch) => mounted && epoch == _epoch && _sameIdentity;
  bool get _disabled => _working || widget.busy || !_sameIdentity;
  List<String> get _actions => ['transfer', 'return', 'countersign']
      .where((key) =>
          widget.detail.statusKey == 'active' &&
          widget.detail.allowedActions.contains(key) &&
          widget.session.can('workflow.todos.$key'))
      .toList();
  bool get _canCancel =>
      _sameIdentity &&
      _chain != null &&
      widget.session.can('workflow.todos.countersign') &&
      widget.detail.allowedActions.contains('countersign') &&
      ({'active', 'before_countersign'}.contains(widget.detail.statusKey) ||
          widget.detail.statusKey == 'completed' &&
              _chain!.direction == 'after');
  WorkflowReminderTarget? get _reminder {
    for (final item in _reminders?.items ?? <WorkflowReminderTarget>[]) {
      if (item.stepId == widget.detail.stepId && item.canRemind) return item;
    }
    return null;
  }

  bool get _ready =>
      !_disabled &&
      !widget.unsaved &&
      _actions.contains(_action) &&
      (!widget.detail.commentRequired || widget.comment.trim().isNotEmpty) &&
      (_action == 'countersign' ||
          !widget.detail.signRequired ||
          widget.signatureFileId != null) &&
      (_action != 'return' || widget.comment.trim().isNotEmpty) &&
      (_action == 'countersign'
          ? _chainLoaded && _chain == null && _members.isNotEmpty
          : _selected != null);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_initialized) {
      _initialized = true;
      _initialize();
    }
  }

  @override
  void didUpdateWidget(covariant WorkflowHumanManagementPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.detail.id != widget.detail.id ||
        oldWidget.detail.revision != widget.detail.revision ||
        oldWidget.client != widget.client ||
        oldWidget.session != widget.session ||
        !_sameIdentity) {
      _initialize();
    }
  }

  @override
  void dispose() {
    _epoch++;
    _reason.dispose();
    super.dispose();
  }

  Future<void> _initialize() async {
    _identity = widget.session.currentUser;
    final current = ++_epoch;
    setState(() {
      _rows = [];
      _members = [];
      _selected = null;
      _chain = null;
      _reminders = null;
      _chainLoaded = false;
      _page = 1;
      _hasNext = false;
      _loading = false;
      _failed = false;
      _feedback = null;
      _key = null;
      _semantic = null;
      _reason.clear();
      if (!_actions.contains(_action) && _actions.isNotEmpty) {
        _action = _actions.first;
      }
    });
    await Future.wait([
      _loadPage(1),
      (() async {
        if (!widget.session.can('workflow.todos.countersign') ||
            !widget.detail.allowedActions.contains('countersign')) {
          return;
        }
        try {
          final value =
              await widget.client.getCountersignChain(widget.detail.id);
          if (_current(current)) {
            setState(() {
              _chain = value;
              _chainLoaded = true;
            });
          }
        } catch (_) {
          if (_current(current)) {
            setState(() => _feedback = AppLocalizations.of(context).loadFailed);
          }
        }
      })(),
      (() async {
        if (!widget.session.can('workflow.instances.remind') ||
            !widget.detail.allowedActions.contains('remind') ||
            widget.detail.statusKey != 'active') {
          return;
        }
        try {
          final value =
              await widget.client.reminderTargets(widget.detail.instanceId);
          if (_current(current)) setState(() => _reminders = value);
        } catch (_) {
          if (_current(current)) {
            setState(() => _feedback = AppLocalizations.of(context).loadFailed);
          }
        }
      })()
    ]);
  }

  Future<void> _loadPage(int next) async {
    if (!_actions.contains(_action) || _disabled || _loading) return;
    final current = _epoch;
    final action = _action;
    setState(() {
      _loading = true;
      _failed = false;
    });
    try {
      List<_Target> rows;
      bool hasNext;
      if (action == 'return') {
        final result =
            await widget.client.listReturnTargets(widget.detail.id, page: next);
        rows = result
            .map((item) => _Target(
                item.stepId, '${item.nodeKey} · ${item.completedAtUtc}'))
            .toList();
        hasNext = result.length == 20;
      } else {
        final result = await widget.client
            .listManagementCandidates(widget.detail.id, action, page: next);
        rows = result.items
            .where((item) => item.id != widget.detail.assigneeUserId)
            .map((item) => _Target(item.id, item.label))
            .toList();
        hasNext = result.page * 20 < result.total;
      }
      if (_current(current)) {
        setState(() {
          _rows = rows;
          _hasNext = hasNext;
          _page = next;
        });
      }
    } catch (_) {
      if (_current(current)) {
        setState(() {
          _failed = true;
          _rows = [];
        });
      }
    } finally {
      if (_current(current)) setState(() => _loading = false);
    }
  }

  Future<void> _execute(String kind) async {
    if (_disabled ||
        widget.unsaved ||
        ({'transfer', 'return', 'countersign'}.contains(kind) && !_ready) ||
        kind == 'cancel' &&
            (!_canCancel ||
                widget.detail.commentRequired && _reason.text.trim().isEmpty) ||
        kind == 'remind' && _reminder == null) {
      return;
    }
    final current = _epoch;
    final detail = widget.detail;
    final comment =
        kind == 'cancel' ? _reason.text.trim() : widget.comment.trim();
    final semantic = jsonEncode([
      kind,
      detail.id,
      detail.revision,
      detail.submissionRevision,
      _selected?.id,
      _direction,
      _members.map((item) => item.id).toList(),
      comment,
      widget.signatureFileId,
      _reminders?.instanceRevision,
      _reminder?.revision
    ]);
    try {
      if (_semantic != semantic) {
        _key = createIdempotencyKey();
        _semantic = semantic;
      }
    } catch (_) {
      setState(() => _feedback = AppLocalizations.of(context).loadFailed);
      return;
    }
    setState(() {
      _working = true;
      _feedback = null;
    });
    widget.onBusy(true);
    try {
      if (kind == 'transfer') {
        await widget.client.transfer(detail.id, _selected!.id,
            revision: detail.revision,
            formRevision: detail.submissionRevision,
            key: _key!,
            comment: comment,
            signatureFileId: widget.signatureFileId);
      } else if (kind == 'return') {
        await widget.client.returnTodo(detail.id, _selected!.id,
            revision: detail.revision,
            formRevision: detail.submissionRevision,
            key: _key!,
            comment: comment,
            signatureFileId: widget.signatureFileId);
      } else if (kind == 'countersign') {
        await widget.client.countersign(
            detail.id, _direction, _members.map((item) => item.id).toList(),
            revision: detail.revision, key: _key!, comment: comment);
      } else if (kind == 'cancel') {
        await widget.client.cancelCountersign(detail.id,
            revision: detail.revision, key: _key!, comment: comment);
      } else {
        await widget.client.remind(detail.instanceId, _reminder!.stepId,
            instanceRevision: _reminders!.instanceRevision,
            stepRevision: _reminder!.revision,
            key: _key!);
      }
      if (!_current(current)) return;
      _key = null;
      _semantic = null;
      if (kind == 'remind') {
        await widget.onRefresh();
      } else {
        widget.onCompleted();
      }
    } catch (error) {
      if (!_current(current)) return;
      final retry = error is! HttpProblem ||
          error.status == 408 ||
          error.status == 429 ||
          error.status >= 500;
      if (!retry) {
        _key = null;
        _semantic = null;
      }
      setState(() => _feedback = AppLocalizations.of(context).loadFailed);
      if (error is HttpProblem && error.status == 409) await widget.onRefresh();
    } finally {
      if (_current(current)) {
        setState(() => _working = false);
        widget.onBusy(false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final rows = [..._rows];
    if (_selected != null && !rows.any((item) => item.id == _selected!.id)) {
      rows.insert(0, _selected!);
    }
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      if (widget.unsaved) Text(l10n.managementLabel('unsaved')),
      if (_actions.isNotEmpty) ...[
        Wrap(spacing: 8, children: [
          for (final action in _actions)
            ChoiceChip(
                key: ValueKey('workflow-management-$action'),
                label: Text(l10n.managementLabel(action)),
                selected: action == _action,
                onSelected: _disabled
                    ? null
                    : (_) {
                        if (_action != action) {
                          _action = action;
                          _initialize();
                        }
                      })
        ]),
        DropdownButtonFormField<String>(
            key: ValueKey('$_action:$_page:${_selected?.id}'),
            initialValue: _selected?.id,
            hint: Text(l10n.managementLabel('choose')),
            isExpanded: true,
            items: rows
                .map((item) => DropdownMenuItem(
                    value: item.id,
                    child: Text(item.label, overflow: TextOverflow.ellipsis)))
                .toList(),
            onChanged: _disabled || _loading
                ? null
                : (id) => setState(() =>
                    _selected = rows.firstWhere((item) => item.id == id))),
        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          TextButton(
              onPressed: _disabled || _loading || _page <= 1
                  ? null
                  : () => _loadPage(_page - 1),
              child: Text(l10n.previousPage)),
          Text('$_page'),
          TextButton(
              onPressed: _disabled || _loading || !_hasNext
                  ? null
                  : () => _loadPage(_page + 1),
              child: Text(l10n.nextPage))
        ]),
        if (_failed)
          TextButton(
              onPressed: _disabled || _loading ? null : () => _loadPage(_page),
              child: Text(l10n.reloadCandidates)),
        if (_action == 'countersign') ...[
          Wrap(spacing: 8, children: [
            for (final direction in ['before', 'after'])
              ChoiceChip(
                  label: Text(l10n.managementLabel(direction)),
                  selected: _direction == direction,
                  onSelected: _disabled
                      ? null
                      : (_) => setState(() => _direction = direction))
          ]),
          TextButton(
              onPressed: _disabled ||
                      _selected == null ||
                      _members.length >= 10 ||
                      _members.any((item) => item.id == _selected!.id)
                  ? null
                  : () => setState(() {
                        _members = [..._members, _selected!];
                        _selected = null;
                      }),
              child: Text(l10n.managementLabel('add'))),
          for (var index = 0; index < _members.length; index++)
            ListTile(
                title: Text('${index + 1}. ${_members[index].label}'),
                trailing: IconButton(
                    tooltip: l10n.managementLabel('remove'),
                    icon: const Icon(Icons.close),
                    onPressed: _disabled
                        ? null
                        : () => setState(() => _members.removeAt(index))))
        ],
        FilledButton(
            key: const ValueKey('workflow-management-submit'),
            onPressed: _ready ? () => _execute(_action) : null,
            child: Text(l10n.managementLabel(_action)))
      ],
      if (_chain != null) ...[
        Text(l10n.managementLabel('chain')),
        for (final item in _chain!.items)
          Text(
              '${item.sequence}. ${item.userId} · ${l10n.managementLabel(item.status)}')
      ],
      if (_canCancel) ...[
        TextField(
            controller: _reason,
            enabled: !_disabled,
            maxLength: 512,
            decoration: InputDecoration(labelText: l10n.comment),
            onChanged: (_) => setState(() {})),
        OutlinedButton(
            key: const ValueKey('workflow-management-cancel'),
            onPressed: _disabled ||
                    widget.unsaved ||
                    widget.detail.commentRequired && _reason.text.trim().isEmpty
                ? null
                : () => _execute('cancel'),
            child: Text(l10n.managementLabel('cancel')))
      ],
      if (_reminder != null)
        OutlinedButton(
            key: const ValueKey('workflow-management-remind'),
            onPressed:
                _disabled || widget.unsaved ? null : () => _execute('remind'),
            child: Text(l10n.managementLabel('remind'))),
      if (_feedback != null) ...[
        Text(_feedback!),
        TextButton(
            onPressed: _disabled ? null : _initialize,
            child: Text(l10n.reloadCandidates))
      ]
    ]);
  }
}
