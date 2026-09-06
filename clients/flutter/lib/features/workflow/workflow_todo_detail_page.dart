import 'package:flutter/material.dart';

import '../../core/auth/identity_session.dart';
import '../../core/idempotency_key.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_models.dart';
import 'workflow_todo_client.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _commentController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final l10n = AppLocalizations.of(context);
    setState(() {
      _loading = true;
      _feedback = null;
    });
    try {
      if (!widget.session.can('workflow.todos.read')) {
        throw StateError('permission-denied');
      }
      final detail = await widget.client.getRuntime(widget.todoId);
      setState(() {
        _detail = detail;
        _idempotencyKey = null;
      });
    } catch (_) {
      setState(() => _feedback = l10n.loadFailed);
    } finally {
      setState(() => _loading = false);
    }
  }

  Future<void> _act(String action) async {
    final l10n = AppLocalizations.of(context);
    final detail = _detail;
    if (detail == null || _submitting || detail.statusKey != 'active') {
      return;
    }
    final permission = action == 'approve'
        ? 'workflow.todos.approve'
        : 'workflow.todos.reject';
    if (!widget.session.can(permission)) {
      setState(() => _feedback = l10n.permissionDenied);
      return;
    }

    _idempotencyKey ??= createIdempotencyKey();
    final request = ActWorkflowTodoRequest(
      expectedRevision: detail.revision,
      fieldPatch: const {},
      comment: _commentController.text.trim().isEmpty
          ? null
          : _commentController.text.trim(),
      idempotencyKey: _idempotencyKey!,
    );

    setState(() {
      _submitting = true;
      _feedback = null;
    });
    try {
      if (action == 'approve') {
        await widget.client.approve(widget.todoId, request);
      } else {
        await widget.client.reject(widget.todoId, request);
      }
      if (!mounted) {
        return;
      }
      Navigator.of(context).pop();
    } catch (_) {
      setState(() => _feedback = l10n.actionFailed);
    } finally {
      setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final detail = _detail;
    final canApprove =
        detail?.statusKey == 'active' && widget.session.can('workflow.todos.approve');
    final canReject =
        detail?.statusKey == 'active' && widget.session.can('workflow.todos.reject');

    return Scaffold(
      appBar: AppBar(title: Text(l10n.workflowTodoTitle)),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : detail == null
              ? Center(child: Text(_feedback ?? l10n.loadFailed))
              : Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('${detail.statusKey} · R${detail.revision}'),
                      const SizedBox(height: 16),
                      Text(detail.submission.toString()),
                      const SizedBox(height: 16),
                      TextField(
                        controller: _commentController,
                        decoration: InputDecoration(labelText: l10n.comment),
                        maxLines: 3,
                      ),
                      if (_feedback != null) ...[
                        const SizedBox(height: 12),
                        Text(
                          _feedback!,
                          style: TextStyle(color: Theme.of(context).colorScheme.error),
                        ),
                      ],
                      const Spacer(),
                      if (canReject)
                        OutlinedButton(
                          onPressed: _submitting ? null : () => _act('reject'),
                          child: Text(l10n.reject),
                        ),
                      if (canApprove) ...[
                        const SizedBox(height: 8),
                        FilledButton(
                          onPressed: _submitting ? null : () => _act('approve'),
                          child: Text(_submitting ? l10n.submitting : l10n.approve),
                        ),
                      ],
                    ],
                  ),
                ),
    );
  }
}
