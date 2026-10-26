import 'package:flutter/material.dart';
import '../../l10n/app_localizations.dart';
import 'workflow_vote_models.dart';

/// 原生选票编辑器只绘制已声明字段；尚未接入原生文件或子表适配的表单不能发送空答案。
class WorkflowVoteFormEditor extends StatefulWidget {
  const WorkflowVoteFormEditor(
      {super.key,
      required this.form,
      required this.formKey,
      required this.enabled,
      required this.onChanged});
  final WorkflowVoteForm form;
  final GlobalKey<FormState> formKey;
  final bool enabled;
  final ValueChanged<Map<String, dynamic>> onChanged;
  static bool supports(WorkflowVoteForm form) =>
      (form.schema['sections'] as List).every((section) =>
          ((section as Map<String, dynamic>)['fields'] as List).every((field) =>
              !{'attachment', 'subtable'}
                  .contains((field as Map<String, dynamic>)['fieldTypeKey'])));
  @override
  State<WorkflowVoteFormEditor> createState() => _WorkflowVoteFormEditorState();
}

class _WorkflowVoteFormEditorState extends State<WorkflowVoteFormEditor> {
  late Map<String, dynamic> values;
  @override
  void initState() {
    super.initState();
    values = Map<String, dynamic>.from(widget.form.values);
  }

  void update(String key, dynamic value) {
    setState(() {
      if (value == null || value is String && value.trim().isEmpty) {
        values.remove(key);
      } else {
        values[key] = value;
      }
    });
    widget.onChanged(Map<String, dynamic>.from(values));
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    if (!WorkflowVoteFormEditor.supports(widget.form)) {
      return Text(l10n.voteFormUnavailable);
    }
    return Form(
        key: widget.formKey,
        child:
            Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          for (final section in widget.form.schema['sections'] as List)
            for (final field
                in (section as Map<String, dynamic>)['fields'] as List)
              Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: buildField(field as Map<String, dynamic>, l10n)),
        ]));
  }

  Widget buildField(Map<String, dynamic> field, AppLocalizations l10n) {
    final key = field['fieldKey'] as String,
        kind = field['fieldTypeKey'] as String;
    final required = widget.form.fieldPolicies[key] == 'required';
    final enabled = widget.enabled && !widget.form.readOnly;
    final constraints = field['constraints'] as Map<String, dynamic>;
    final label = required ? '$key *' : key;
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
