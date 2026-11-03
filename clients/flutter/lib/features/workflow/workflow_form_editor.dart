import 'package:flutter/material.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_form_projection.dart';

/// 只绘制当前授权字段；主表单和独立选票分别拥有自己的投影与回写。
class WorkflowFormEditor extends StatefulWidget {
  const WorkflowFormEditor(
      {super.key,
      required this.form,
      this.formKey,
      required this.enabled,
      required this.onChanged,
      this.embedded = false,
      this.uploadAttachment,
      this.downloadAttachment,
      this.attachmentCandidates = const {}});
  final WorkflowFormProjection form;
  final GlobalKey<FormState>? formKey;
  final bool enabled;
  final ValueChanged<Map<String, dynamic>> onChanged;
  final bool embedded;
  final Future<({String fileId, String name})?> Function(String fieldKey)?
      uploadAttachment;
  final Future<void> Function(String fileId, String name)? downloadAttachment;
  final Map<String, String> attachmentCandidates;
  @override
  State<WorkflowFormEditor> createState() => _WorkflowFormEditorState();
}

class _WorkflowFormEditorState extends State<WorkflowFormEditor> {
  late Map<String, dynamic> values;
  final _localNames = <String, String>{};
  final _rowKeys = <String, List<Key>>{};
  var _uploading = false;
  bool _fileFailed = false;
  @override
  void initState() {
    super.initState();
    values = cloneWorkflowValues(widget.form.values);
  }

  void update(String key, dynamic value) {
    setState(() {
      if (value == null || value is String && value.trim().isEmpty) {
        values.remove(key);
      } else {
        values[key] = value;
      }
    });
    widget.onChanged(cloneWorkflowValues(values));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final content =
        Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      for (final section in widget.form.schema['sections'] as List)
        for (final field in (section as Map<String, dynamic>)['fields'] as List)
          Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: buildField(field as Map<String, dynamic>, l10n)),
    ]);
    return widget.embedded
        ? content
        : Form(key: widget.formKey, child: content);
  }

  Widget buildField(Map<String, dynamic> field, AppLocalizations l10n) {
    final key = field['fieldKey'] as String,
        kind = field['fieldTypeKey'] as String;
    final required = widget.form.fieldPolicies[key] == 'required';
    if (widget.form.fieldPolicies[key] == 'hidden') {
      return const SizedBox.shrink();
    }
    final enabled = widget.enabled &&
        !_uploading &&
        !widget.form.readOnly &&
        {'required', 'editable'}.contains(widget.form.fieldPolicies[key]);
    final constraints = field['constraints'] as Map<String, dynamic>;
    final label = required ? '$key *' : key;
    if (kind == 'attachment') {
      final ids = List<String>.from(values[key] as List? ?? const []);
      final max = constraints['maxCount'] as int;
      String name(String id) =>
          _localNames[id] ??
          widget.form.attachmentNames[id] ??
          widget.attachmentCandidates[id] ??
          l10n.attachment;
      return FormField<List<String>>(
          initialValue: ids,
          validator: (_) => required && ids.isEmpty ? l10n.requiredField : null,
          builder: (state) =>
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(label),
                for (final id in ids)
                  Wrap(
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        TextButton(
                            onPressed: widget.downloadAttachment == null
                                ? null
                                : () async {
                                    try {
                                      await widget.downloadAttachment!(
                                          id, name(id));
                                    } catch (_) {
                                      if (mounted) {
                                        setState(() => _fileFailed = true);
                                      }
                                    }
                                  },
                            child: Text(name(id))),
                        if (enabled)
                          IconButton(
                              tooltip: l10n.removeAttachment,
                              icon: const Icon(Icons.close),
                              onPressed: () {
                                final next = [...ids]..remove(id);
                                update(key, next);
                                state.didChange(next);
                              }),
                      ]),
                if (enabled && ids.length < max) ...[
                  for (final entry in widget.attachmentCandidates.entries
                      .where((entry) => !ids.contains(entry.key)))
                    TextButton(
                        onPressed: () {
                          final next = [...ids, entry.key];
                          update(key, next);
                          state.didChange(next);
                        },
                        child: Text(
                            '${l10n.reuseAttachment}: ${name(entry.key)}')),
                  if (widget.uploadAttachment != null)
                    OutlinedButton(
                        onPressed: () async {
                          setState(() => _uploading = true);
                          try {
                            final picked = await widget.uploadAttachment!(key);
                            if (!mounted ||
                                picked == null ||
                                widget.form.readOnly ||
                                !{'required', 'editable'}
                                    .contains(widget.form.fieldPolicies[key])) {
                              return;
                            }
                            final current =
                                List<String>.from(values[key] as List? ?? []);
                            if (current.length < max &&
                                !current.contains(picked.fileId)) {
                              _localNames[picked.fileId] = picked.name;
                              final next = [...current, picked.fileId];
                              update(key, next);
                              state.didChange(next);
                            }
                          } catch (_) {
                            if (mounted) setState(() => _fileFailed = true);
                          } finally {
                            if (mounted) setState(() => _uploading = false);
                          }
                        },
                        child: Text(l10n.uploadAttachment)),
                ],
                if (state.hasError)
                  Text(state.errorText!,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error)),
                if (_fileFailed)
                  Text(l10n.attachmentFailed,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error)),
              ]));
    }
    if (kind == 'subtable') {
      final rows = (values[key] as List? ?? []).cast<Map<String, dynamic>>();
      final columns =
          (constraints['columns'] as List).cast<Map<String, dynamic>>();
      final rowKeys = _rowKeys.putIfAbsent(
          key, () => [for (var i = 0; i < rows.length; i++) UniqueKey()]);
      return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        Text(label),
        FormField<int>(
            initialValue: rows.length,
            validator: (_) =>
                required && rows.isEmpty ? l10n.requiredField : null,
            builder: (state) => Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      for (var i = 0; i < rows.length; i++)
                        Card(
                            child: Padding(
                                padding: const EdgeInsets.all(12),
                                child: Column(children: [
                                  WorkflowFormEditor(
                                      key: rowKeys[i],
                                      embedded: true,
                                      enabled: enabled,
                                      form: WorkflowFormProjection(
                                          schema: {
                                            'sections': [
                                              {
                                                'fields': [
                                                  for (final column in columns)
                                                    {
                                                      ...column,
                                                      'fieldKey':
                                                          column['columnKey']
                                                    }
                                                ]
                                              }
                                            ]
                                          },
                                          values: cloneWorkflowValues(rows[i]),
                                          fieldPolicies: {
                                            for (final column in columns)
                                              column['columnKey'] as String:
                                                  column['required'] == true
                                                      ? 'required'
                                                      : 'editable'
                                          },
                                          readOnly: !enabled),
                                      onChanged: (row) {
                                        final next = [
                                          for (final existing in rows)
                                            cloneWorkflowValues(existing)
                                        ];
                                        next[i] = row;
                                        update(key, next);
                                      }),
                                  if (enabled)
                                    TextButton(
                                        onPressed: () {
                                          final next = [...rows]..removeAt(i);
                                          rowKeys.removeAt(i);
                                          update(key, next);
                                          state.didChange(next.length);
                                        },
                                        child: Text(l10n.removeRow)),
                                ]))),
                      if (enabled &&
                          rows.length < (constraints['maxRows'] as int))
                        OutlinedButton(
                            onPressed: () {
                              rowKeys.add(UniqueKey());
                              final next = [...rows, <String, dynamic>{}];
                              update(key, next);
                              state.didChange(next.length);
                            },
                            child: Text(l10n.addRow)),
                      if (state.hasError)
                        Text(state.errorText!,
                            style: TextStyle(
                                color: Theme.of(context).colorScheme.error)),
                    ])),
      ]);
    }
    if (kind == 'switch') {
      return FormField<bool>(
          initialValue: values[key] as bool?,
          validator: (value) =>
              required && value == null ? l10n.requiredField : null,
          builder: (state) =>
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                SwitchListTile(
                    title: Text(label),
                    value: values[key] == true,
                    onChanged: !enabled
                        ? null
                        : (value) {
                            update(key, value);
                            state.didChange(value);
                          }),
                if (state.hasError)
                  Text(state.errorText!,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error)),
              ]));
    }
    if (kind == 'radio' || kind == 'select') {
      final options = (constraints['options'] as List).cast<String>();
      return DropdownButtonFormField<String>(
          key: ValueKey(key),
          initialValue: values[key] as String?,
          decoration: InputDecoration(labelText: label),
          items: options
              .map((option) =>
                  DropdownMenuItem(value: option, child: Text(option)))
              .toList(),
          onChanged: enabled ? (value) => update(key, value) : null,
          validator: (value) =>
              required && value == null ? l10n.requiredField : null);
    }
    if (kind == 'checkbox') {
      final options = (constraints['options'] as List).cast<String>();
      final selected = List<String>.from(values[key] as List? ?? const []);
      return FormField<List<String>>(
          initialValue: selected,
          validator: (value) => required && (value == null || value.isEmpty)
              ? l10n.requiredField
              : null,
          builder: (state) =>
              Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(label),
                for (final option in options)
                  CheckboxListTile(
                      title: Text(option),
                      value: selected.contains(option),
                      onChanged: !enabled
                          ? null
                          : (checked) {
                              final next = [...selected];
                              if (checked == true) {
                                next.add(option);
                              } else {
                                next.remove(option);
                              }
                              update(key, next);
                              state.didChange(next);
                            }),
                if (state.hasError)
                  Text(state.errorText!,
                      style: TextStyle(
                          color: Theme.of(context).colorScheme.error)),
              ]));
    }
    return TextFormField(
        key: ValueKey(key),
        initialValue: values[key]?.toString() ?? '',
        enabled: enabled,
        maxLines: kind == 'textarea' ? 3 : 1,
        keyboardType: {'integer', 'money', 'decimal'}.contains(kind)
            ? TextInputType.numberWithOptions(
                signed: true, decimal: kind != 'integer')
            : TextInputType.text,
        decoration: InputDecoration(labelText: label),
        onChanged: (value) => update(
            key,
            kind == 'integer' && value.isNotEmpty
                ? int.tryParse(value) ?? value
                : {'money', 'decimal', 'date', 'time', 'datetime'}
                        .contains(kind)
                    ? value.trim()
                    : value),
        validator: (text) {
          final value = text?.trim() ?? '';
          if (value.isEmpty) {
            return required ? l10n.requiredField : null;
          }
          if (kind == 'integer' && int.tryParse(value) == null) {
            return l10n.invalidField;
          }
          // 金额和高精度小数保留规范字符串，不能转换为double损失精度。
          if ((kind == 'money' || kind == 'decimal') &&
              (!RegExp(r'^-?(0|[1-9]\d*)(\.\d+)?$').hasMatch(value) ||
                  (value.contains('.') ? value.split('.').last.length : 0) >
                      (constraints['scale'] as int))) {
            return l10n.invalidField;
          }
          if (kind == 'date' &&
              (!RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(value) ||
                  DateTime.tryParse(value) == null)) {
            return l10n.invalidField;
          }
          if (kind == 'time' &&
              !RegExp(r'^([01]\d|2[0-3]):[0-5]\d(:[0-5]\d)?$')
                  .hasMatch(value)) {
            return l10n.invalidField;
          }
          if (kind == 'datetime' && DateTime.tryParse(value) == null) {
            return l10n.invalidField;
          }
          if ((kind == 'text' || kind == 'textarea') &&
              (value.length < (constraints['minLength'] as int? ?? 0) ||
                  value.length >
                      (constraints['maxLength'] as int? ?? 2147483647))) {
            return l10n.invalidField;
          }
          return null;
        });
  }
}
