import 'package:flutter/material.dart';
import '../../core/auth/identity_session.dart';
import '../../core/idempotency_key.dart';
import '../../core/http/http_problem.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_claim_models.dart';
import 'workflow_todo_client.dart';
import 'workflow_todo_detail_page.dart';

/// 候选池与个人待办分开展示，服务器确认领取后才能进入实际办理页面。
class WorkflowClaimPoolsPage extends StatefulWidget {
  const WorkflowClaimPoolsPage(
      {super.key, required this.session, required this.client});
  final IdentitySession session;
  final WorkflowTodoClient client;
  @override
  State<WorkflowClaimPoolsPage> createState() => _WorkflowClaimPoolsPageState();
}

class _WorkflowClaimPoolsPageState extends State<WorkflowClaimPoolsPage> {
  List<WorkflowClaimPool> _items = const [];
  final _pending = <String, ({int revision, String key})>{};
  var _loading = false;
  var _initialized = false;
  String? _acting, _error;
  int _epoch = 0;
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_initialized) {
      _initialized = true;
      _load();
    }
  }

  @override
  void dispose() {
    _epoch++;
    super.dispose();
  }

  Future<void> _load() async {
    final l10n = AppLocalizations.of(context);
    final current = ++_epoch;
    if (!widget.session.can('workflow.claim_pools.read')) {
      setState(() {
        _items = [];
        _error = l10n.permissionDenied;
      });
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final items = await widget.client.listClaimPools();
      if (mounted && current == _epoch) setState(() => _items = items);
    } catch (_) {
      if (mounted && current == _epoch) {
        setState(() => _error = l10n.loadFailed);
      }
    } finally {
      if (mounted && current == _epoch) setState(() => _loading = false);
    }
  }

  Future<void> _claim(WorkflowClaimPool pool) async {
    if (_acting != null || !widget.session.can('workflow.claim_pools.claim')) {
      return;
    }
    final l10n = AppLocalizations.of(context);
    setState(() {
      _acting = pool.id;
      _error = null;
    });
    try {
      var operation = _pending[pool.id];
      if (operation?.revision != pool.revision) {
        operation = (revision: pool.revision, key: createIdempotencyKey());
        _pending[pool.id] = operation;
      }
      final todo = await widget.client
          .claimPool(pool.id, operation!.revision, operation.key);
      if (!mounted) return;
      _pending.remove(pool.id);
      if (widget.session.can('workflow.todos.read')) {
        await Navigator.of(context).push(MaterialPageRoute<void>(
            builder: (_) => WorkflowTodoDetailPage(
                todoId: todo, session: widget.session, client: widget.client)));
      }
      if (mounted) await _load();
    } catch (error) {
      // 结果未知时保留同一请求键，手动刷新后服务器新修订会生成新操作。
      if (error is HttpProblem && error.status < 500) {
        _pending.remove(pool.id);
        if (mounted && error.status == 409) await _load();
      }
      if (mounted) setState(() => _error = l10n.actionFailed);
    } finally {
      if (mounted) setState(() => _acting = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
        appBar: AppBar(title: Text(l10n.claimPoolsTitle), actions: [
          IconButton(
              onPressed: _loading || _acting != null ? null : _load,
              tooltip: l10n.claimPoolsTitle,
              icon: const Icon(Icons.refresh))
        ]),
        body: Column(children: [
          if (_error != null)
            Padding(
                padding: const EdgeInsets.all(16),
                child: Text(_error!,
                    style:
                        TextStyle(color: Theme.of(context).colorScheme.error))),
          if (_loading) const LinearProgressIndicator(),
          Expanded(
              child: _items.isEmpty && !_loading
                  ? Center(child: Text(l10n.emptyTodos))
                  : ListView.separated(
                      itemCount: _items.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final pool = _items[index];
                        return ListTile(
                            title: Text(pool.title),
                            subtitle: Text(pool.dueAtUtc ?? pool.createdAtUtc),
                            trailing: !widget.session
                                    .can('workflow.claim_pools.claim')
                                ? null
                                : FilledButton(
                                    onPressed: _acting == null
                                        ? () => _claim(pool)
                                        : null,
                                    child: Text(_acting == pool.id
                                        ? l10n.submitting
                                        : l10n.claimTask)));
                      }))
        ]));
  }
}
