import 'dart:convert';

/// 主表单与独立选票共享字段投影，不共享提交修订、人员或答案事实。
class WorkflowFormProjection {
  const WorkflowFormProjection(
      {required this.schema,
      required this.values,
      required this.fieldPolicies,
      required this.readOnly,
      this.attachmentNames = const {}});
  final Map<String, dynamic> schema, values;
  final Map<String, String> fieldPolicies, attachmentNames;
  final bool readOnly;

  /// 控件重建使用页面草稿，权威基线仍独立保留；中间输入先由Form校验，再生成Patch。
  WorkflowFormProjection withValues(Map<String, dynamic> draft,
          {Map<String, String>? names}) =>
      WorkflowFormProjection(
          schema: schema,
          values: cloneWorkflowValues(draft),
          fieldPolicies: fieldPolicies,
          readOnly: readOnly,
          attachmentNames: names ?? attachmentNames);

  bool hasChanges(Map<String, dynamic> candidate) {
    if (readOnly) return false;
    return fields.any((field) {
      final key = field['fieldKey'] as String;
      return {'required', 'editable'}.contains(fieldPolicies[key]) &&
          jsonEncode(candidate[key]) != jsonEncode(values[key]);
    });
  }

  static const scalarKinds = {
    'text',
    'textarea',
    'integer',
    'money',
    'decimal',
    'date',
    'time',
    'datetime',
    'radio',
    'select',
    'checkbox',
    'switch'
  };
  static final guid = RegExp(
      r'^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
      caseSensitive: false);
  Iterable<Map<String, dynamic>> get fields => (schema['sections'] as List)
      .expand((section) => (section as Map<String, dynamic>)['fields'] as List)
      .cast<Map<String, dynamic>>();

  factory WorkflowFormProjection.parse(
      dynamic schema, dynamic values, dynamic policies,
      {required bool readOnly, dynamic attachmentNames}) {
    if (schema is! Map<String, dynamic> ||
        schema['schemaVersion'] != 1 ||
        schema['adapterVersion'] != 1 ||
        schema['sections'] is! List ||
        (schema['sections'] as List).length > 32 ||
        values is! Map<String, dynamic> ||
        policies is! Map<String, dynamic>) {
      throw const FormatException('Invalid workflow form projection');
    }
    final keys = <String>{};
    final policy = <String, String>{};
    final visibleFiles = <String>{};
    for (final section in schema['sections'] as List) {
      if (section is! Map<String, dynamic> ||
          section['sectionKey'] is! String ||
          section['fields'] is! List) {
        throw const FormatException('Invalid workflow form section');
      }
      for (final raw in section['fields'] as List) {
        if (raw is! Map<String, dynamic> ||
            raw['fieldKey'] is! String ||
            raw['required'] is! bool ||
            raw['constraints'] is! Map<String, dynamic> ||
            !{...scalarKinds, 'attachment', 'subtable'}
                .contains(raw['fieldTypeKey']) ||
            !keys.add(raw['fieldKey'] as String) ||
            keys.length > 256) {
          throw const FormatException('Invalid workflow field');
        }
        final key = raw['fieldKey'] as String;
        final current = policies[key] ??
            (raw['required'] == true ? 'required' : 'editable');
        if (!{'hidden', 'readOnly', 'editable', 'required'}.contains(current)) {
          throw const FormatException('Invalid workflow field policy');
        }
        policy[key] =
            readOnly && current != 'hidden' ? 'readOnly' : current as String;
        validateValue(raw, values[key]);
        if (raw['fieldTypeKey'] == 'attachment' && current != 'hidden') {
          visibleFiles.addAll((values[key] as List? ?? []).cast<String>());
        }
      }
    }
    if (values.keys.any((key) => !keys.contains(key)) ||
        policies.keys.any((key) => !keys.contains(key))) {
      throw const FormatException('Unknown workflow field');
    }
    final names = <String, String>{};
    if (attachmentNames != null) {
      if (attachmentNames is! Map<String, dynamic> ||
          attachmentNames.length > 256) {
        throw const FormatException('Invalid attachment names');
      }
      for (final entry in attachmentNames.entries) {
        if (!visibleFiles.contains(entry.key) ||
            entry.value is! String ||
            (entry.value as String).isEmpty ||
            (entry.value as String).length > 256) {
          throw const FormatException('Invalid attachment name');
        }
        names[entry.key] = entry.value as String;
      }
    }
    return WorkflowFormProjection(
        schema: cloneWorkflowValues(schema),
        values: cloneWorkflowValues(values),
        fieldPolicies: Map.unmodifiable(policy),
        readOnly: readOnly,
        attachmentNames: Map.unmodifiable(names));
  }

  static void validateValue(Map<String, dynamic> field, dynamic value) {
    final kind = field['fieldTypeKey'];
    final constraints = field['constraints'] as Map<String, dynamic>;
    if (kind == 'subtable') {
      final columns = constraints['columns'];
      final max = constraints['maxRows'];
      final keys = <String>{};
      if (max is! int ||
          max < 1 ||
          max > 20 ||
          columns is! List ||
          columns.isEmpty ||
          columns.length > 16) {
        throw const FormatException('Invalid subtable constraints');
      }
      for (final column in columns) {
        if (column is! Map<String, dynamic> ||
            column['columnKey'] is! String ||
            !keys.add(column['columnKey'] as String) ||
            !scalarKinds.contains(column['fieldTypeKey']) ||
            column['required'] is! bool ||
            column['constraints'] is! Map<String, dynamic>) {
          throw const FormatException('Invalid subtable column');
        }
      }
      if (value == null) return;
      if (value is! List || value.length > max) {
        throw const FormatException('Invalid subtable rows');
      }
      for (final row in value) {
        if (row is! Map<String, dynamic> ||
            row.keys.any((key) => !keys.contains(key))) {
          throw const FormatException('Invalid subtable row');
        }
        for (final column in columns) {
          validateValue(
              column as Map<String, dynamic>, row[column['columnKey']]);
        }
      }
      return;
    }
    if (kind == 'attachment') {
      final max = constraints['maxCount'];
      if (max is! int || max < 1 || max > 20) {
        throw const FormatException('Invalid attachment constraints');
      }
      if (value != null &&
          (value is! List ||
              value.length > max ||
              value.toSet().length != value.length ||
              value.any((id) => id is! String || !guid.hasMatch(id)))) {
        throw const FormatException('Invalid attachment references');
      }
      return;
    }
    if (value == null) return;
    final valid = switch (kind) {
      'integer' => value is int,
      'switch' => value is bool,
      'checkbox' => value is List &&
          value.every((item) =>
              item is String &&
              (constraints['options'] as List).contains(item)) &&
          value.toSet().length == value.length,
      'radio' ||
      'select' =>
        value is String && (constraints['options'] as List).contains(value),
      _ => value is String,
    };
    if (!valid) throw const FormatException('Invalid workflow scalar value');
  }

  /// 只提交真正改变且可编辑的主字段；隐藏、只读和未知字段不进入Patch。
  Map<String, dynamic> patch(Map<String, dynamic> candidate) {
    if (readOnly) return {};
    if (candidate.keys.any((key) => !fieldPolicies.containsKey(key))) {
      throw const FormatException('Unknown workflow field');
    }
    final result = <String, dynamic>{};
    for (final field in fields) {
      final key = field['fieldKey'] as String;
      if (!{'required', 'editable'}.contains(fieldPolicies[key])) continue;
      if (jsonEncode(candidate[key]) != jsonEncode(values[key])) {
        validateValue(field, candidate[key]);
        result[key] = candidate[key];
      }
    }
    return cloneWorkflowValues(result);
  }
}

/// 递归复制嵌套行和多选集合，编辑草稿不得修改已读取的权威投影。
Map<String, dynamic> cloneWorkflowValues(Map<String, dynamic> value) =>
    jsonDecode(jsonEncode(value)) as Map<String, dynamic>;
