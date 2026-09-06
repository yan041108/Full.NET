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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final l10n = AppLocalizations.of(context);
    if (_loading) {
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      if (!widget.session.can('workflow.todos.read')) {
        setState(() => _error = l10n.permissionDenied);
        return;
      }
      final items = await widget.client.listMine();
      setState(() => _items = items);
    } catch (_) {
      setState(() => _error = l10n.loadFailed);
    } finally {
      setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.workflowTodosTitle),
        actions: [
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
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? Center(child: Text(_error!))
              : _items.isEmpty
                  ? Center(child: Text(l10n.emptyTodos))
                  : ListView.separated(
                      itemCount: _items.length,
                      separatorBuilder: (_, __) => const Divider(height: 1),
                      itemBuilder: (context, index) {
                        final item = _items[index];
                        return ListTile(
                          title: Text(item.definitionKey),
                          subtitle: Text('${item.statusKey} · ${item.arrivedAtUtc}'),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () {
                            Navigator.of(context).push(
                              MaterialPageRoute<void>(
                                builder: (_) => WorkflowTodoDetailPage(
                                  todoId: item.id,
                                  session: widget.session,
                                  client: widget.client,
                                ),
                              ),
                            );
                          },
                        );
                      },
                    ),
    );
  }
}
