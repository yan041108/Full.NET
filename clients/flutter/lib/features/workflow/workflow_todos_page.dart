import 'workflow_claim_pools_page.dart';
import 'package:flutter/material.dart';

import '../../core/auth/identity_session.dart';
import '../../features/workflow/workflow_todo_client.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_models.dart';
import 'workflow_todo_detail_page.dart';

class WorkflowTodosPage extends StatefulWidget {
  const WorkflowTodosPage({
    super.key,
    required this.session,
    required this.client,
    required this.onSignedOut,
  });

  final IdentitySession session;
  final WorkflowTodoClient client;
  final VoidCallback onSignedOut;

  @override
  State<WorkflowTodosPage> createState() => _WorkflowTodosPageState();
}

class _WorkflowTodosPageState extends State<WorkflowTodosPage> {
  var _loading = false;
  String? _error;
  List<WorkflowTodoListItem> _items = const [];
  String _queue = 'pending';
  int _page = 1;
  int _total = 0;
  int _epoch = 0;

  @override
  void initState() {
    super.initState();
    widget.session.addListener(_identityChanged);
  }

  void _identityChanged() {
    _epoch++;
    if (!mounted) return;
    setState(() {
      _items = [];
      _loading = false;
      _error = null;
      _page = 1;
      _total = 0;
    });
    if (_initialized && widget.session.can('workflow.todos.read')) _load();
  }

  @override
  void didUpdateWidget(covariant WorkflowTodosPage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.session != widget.session) {
      oldWidget.session.removeListener(_identityChanged);
      widget.session.addListener(_identityChanged);
    }
    if (oldWidget.session != widget.session ||
        oldWidget.client != widget.client) {
      _identityChanged();
    }
  }

  @override
  void dispose() {
    _epoch++;
    widget.session.removeListener(_identityChanged);
    super.dispose();
  }

  var _initialized = false;
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_initialized) {
      _initialized = true;
      _load();
    }
  }

  Future<void> _load() async {
    final l10n = AppLocalizations.of(context);
    if (_loading) {
      return;
    }
    final epoch = ++_epoch;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      if (!widget.session.can('workflow.todos.read')) {
        setState(() => _error = l10n.permissionDenied);
        return;
      }
      if (_queue != 'pending') {
        final result = _queue == 'delegated'
            ? await widget.client.listDelegated(page: _page)
            : await widget.client.listHistory(page: _page);
        if (mounted && epoch == _epoch) {
          setState(() {
            _items = result.items;
            _page = result.page;
            _total = result.total;
          });
        }
      } else {
        final items = await widget.client.listMine();
        if (mounted && epoch == _epoch) setState(() => _items = items);
      }
    } catch (_) {
      if (mounted && epoch == _epoch) setState(() => _error = l10n.loadFailed);
    } finally {
      if (mounted && epoch == _epoch) setState(() => _loading = false);
    }
  }

  void _selectQueue(String queue) {
    if (_loading || _queue == queue) return;
    setState(() {
      _queue = queue;
      _page = 1;
      _total = 0;
      _items = [];
    });
    _load();
  }

  void _changePage(int page) {
    if (_loading || page < 1 || page > _page && (page - 1) * 20 >= _total) {
      return;
    }
    setState(() => _page = page);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.workflowTodosTitle),
        actions: [
          if (widget.session.can('workflow.claim_pools.read'))
            IconButton(
                tooltip: l10n.claimPoolsTitle,
                icon: const Icon(Icons.assignment_ind_outlined),
                onPressed: () async {
                  await Navigator.of(context).push(MaterialPageRoute<void>(
                      builder: (_) => WorkflowClaimPoolsPage(
                          session: widget.session, client: widget.client)));
                  if (mounted) await _load();
                }),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
          IconButton(
            onPressed: () {
              widget.session.logout();
              widget.onSignedOut();
            },
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: Column(children: [
        if (widget.session.can('workflow.todos.read'))
          Row(children: [
            Expanded(
                child: TextButton(
                    key: const ValueKey('workflow-pending-tab'),
                    onPressed: _loading ? null : () => _selectQueue('pending'),
                    child: Text(l10n.pendingTasks))),
            Expanded(
                child: TextButton(
                    key: const ValueKey('workflow-delegated-tab'),
                    onPressed:
                        _loading ? null : () => _selectQueue('delegated'),
                    child: Text(l10n.delegatedTodos))),
            Expanded(
                child: TextButton(
                    key: const ValueKey('workflow-history-tab'),
                    onPressed: _loading ? null : () => _selectQueue('history'),
                    child: Text(l10n.managementLabel('history'))))
          ]),
        Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(child: Text(_error!))
                    : _items.isEmpty
                        ? Center(child: Text(l10n.emptyTodos))
                        : ListView.separated(
                            itemCount: _items.length,
                            separatorBuilder: (_, __) =>
                                const Divider(height: 1),
                            itemBuilder: (context, index) {
                              final item = _items[index];
                              return ListTile(
                                title: Text(item.definitionKey),
                                subtitle: Text(
                                    '${item.statusKey} · ${item.arrivedAtUtc}'),
                                trailing: const Icon(Icons.chevron_right),
                                onTap: () async {
                                  await Navigator.of(context).push(
                                    MaterialPageRoute<void>(
                                      builder: (_) => WorkflowTodoDetailPage(
                                        todoId: item.id,
                                        session: widget.session,
                                        client: widget.client,
                                      ),
                                    ),
                                  );
                                  if (mounted) await _load();
                                },
                              );
                            },
                          )),
        if (_queue != 'pending' && (_total > 20 || _page > 1))
          Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
            TextButton(
                onPressed: _loading || _page <= 1
                    ? null
                    : () => _changePage(_page - 1),
                child: Text(l10n.previousPage)),
            Text('$_page / ${(_total / 20).ceil().clamp(1, 2147483647)}'),
            TextButton(
                onPressed: _loading || _page * 20 >= _total
                    ? null
                    : () => _changePage(_page + 1),
                child: Text(l10n.nextPage))
          ])
      ]),
    );
  }
}
