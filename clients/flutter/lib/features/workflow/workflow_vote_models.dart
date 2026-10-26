/// 独立票据的静态客户端契约；个人修订和答案不能借用主表单。
class WorkflowVoteForm {
  const WorkflowVoteForm(
      {required this.formVersionId,
      required this.formSchemaHash,
      required this.schema,
      required this.values,
      required this.fieldPolicies,
      required this.revision,
      required this.choiceKey,
      required this.readOnly,
      required this.allowAbstain});
  final String formVersionId;
  final String formSchemaHash;
  final Map<String, dynamic> schema;
  final Map<String, dynamic> values;
  final Map<String, String> fieldPolicies;
  final int revision;
  final String? choiceKey;
  final bool readOnly;
  final bool allowAbstain;

  factory WorkflowVoteForm.fromJson(Map<String, dynamic> json) {
    final version = json['formVersionId'];
    final hash = json['formSchemaHash'];
    final schema = json['formSchema'];
    final values = json['values'];
    final policies = json['fieldPolicies'];
    final revision = json['revision'];
    final choice = json['choiceKey'];
    if (version is! String ||
        !RegExp(r'^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$',
                caseSensitive: false)
            .hasMatch(version) ||
        hash is! String ||
        !RegExp(r'^[0-9a-f]{64}$').hasMatch(hash) ||
        schema is! Map<String, dynamic> ||
        schema['schemaVersion'] != 1 ||
        schema['adapterVersion'] != 1 ||
        values is! Map<String, dynamic> ||
        policies is! Map<String, dynamic> ||
        revision is! int ||
        revision < 1 ||
        json['readOnly'] is! bool ||
        json['allowAbstain'] is! bool ||
        choice != null && !{'yes', 'no', 'abstain'}.contains(choice) ||
        choice == 'abstain' && json['allowAbstain'] != true ||
        choice != null && json['readOnly'] != true) {
      throw const FormatException('Invalid private ballot form');
    }
    final sections = schema['sections'];
    final keys = <String>{};
    if (sections is! List || sections.isEmpty || sections.length > 32) {
      throw const FormatException('Invalid ballot sections');
    }
    const kinds = {
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
      'switch',
      'attachment',
      'subtable'
    };
    for (final section in sections) {
      if (section is! Map<String, dynamic> ||
          section['sectionKey'] is! String ||
          section['fields'] is! List) {
        throw const FormatException('Invalid ballot section');
      }
      for (final field in section['fields'] as List) {
        if (field is! Map<String, dynamic> ||
            field['fieldKey'] is! String ||
            !kinds.contains(field['fieldTypeKey']) ||
            field['required'] is! bool ||
            field['constraints'] is! Map<String, dynamic> ||
            !keys.add(field['fieldKey'] as String) ||
            keys.length > 256) {
          throw const FormatException('Invalid ballot field');
        }
      }
    }
    final readOnly = json['readOnly'] as bool;
    if (keys.isEmpty ||
        values.keys.any((key) => !keys.contains(key)) ||
        policies.length != keys.length ||
        policies.entries.any((entry) =>
            !keys.contains(entry.key) ||
            (readOnly
                ? entry.value != 'readOnly'
                : !{'required', 'editable'}.contains(entry.value)))) {
      throw const FormatException('Invalid private ballot field policy');
    }
    return WorkflowVoteForm(
        formVersionId: version,
        formSchemaHash: hash,
        schema: Map.unmodifiable(schema),
        values: Map.unmodifiable(values),
        fieldPolicies: Map.unmodifiable(policies.cast<String, String>()),
        revision: revision,
        choiceKey: choice as String?,
        readOnly: readOnly,
        allowAbstain: json['allowAbstain'] as bool);
  }
}

/// 活动中不能展示选择汇总，闭合后五类数量必须保持固定候选分母。
class WorkflowVoteProgress {
  const WorkflowVoteProgress(
      {required this.memberCount,
      required this.submittedCount,
      required this.pendingCount,
      required this.cancelledCount,
      required this.requiredParticipationCount,
      required this.statusKey,
      required this.closesAtUtc,
      required this.yesCount,
      required this.noCount,
      required this.abstainCount,
      required this.unsubmittedCount});
  final int memberCount,
      submittedCount,
      pendingCount,
      cancelledCount,
      requiredParticipationCount;
  final String statusKey;
  final String? closesAtUtc;
  final int? yesCount, noCount, abstainCount, unsubmittedCount;
  factory WorkflowVoteProgress.fromJson(Map<String, dynamic> json) {
    int count(String key) {
      final value = json[key];
      if (value is! int || value < 0 || value > 20) {
        throw const FormatException('Invalid ballot count');
      }
      return value;
    }

    final total = count('memberCount'),
        submitted = count('submittedCount'),
        pending = count('pendingCount'),
        cancelled = count('cancelledCount'),
        required = count('requiredParticipationCount');
    final status = json['statusKey'], closes = json['closesAtUtc'];
    if (total < 1 ||
        required < 1 ||
        required > total ||
        submitted + pending + cancelled != total ||
        !{'active', 'approved', 'rejected', 'suspended', 'cancelled'}
            .contains(status) ||
        closes != null &&
            (closes is! String || DateTime.tryParse(closes) == null)) {
      throw const FormatException('Invalid vote progress');
    }
    int? yes, no, abstain, unsubmitted;
    if (status == 'active') {
      if (['yesCount', 'noCount', 'abstainCount', 'unsubmittedCount']
              .any((key) => json[key] != null) ||
          cancelled != 0) {
        throw const FormatException(
            'Active vote cannot disclose choice totals');
      }
    } else {
      yes = count('yesCount');
      no = count('noCount');
      abstain = count('abstainCount');
      unsubmitted = count('unsubmittedCount');
      if (yes + no + abstain != submitted ||
          unsubmitted != pending + cancelled) {
        throw const FormatException('Inconsistent vote totals');
      }
    }
    return WorkflowVoteProgress(
        memberCount: total,
        submittedCount: submitted,
        pendingCount: pending,
        cancelledCount: cancelled,
        requiredParticipationCount: required,
        statusKey: status as String,
        closesAtUtc: closes as String?,
        yesCount: yes,
        noCount: no,
        abstainCount: abstain,
        unsubmittedCount: unsubmitted);
  }
}

/// 完整个人答案与选择一同送达，后端仍独立校验表单、身份、修订及证据。
class VoteWorkflowTodoRequest {
  const VoteWorkflowTodoRequest(
      {required this.expectedRevision,
      required this.expectedBallotRevision,
      required this.choiceKey,
      required this.values,
      required this.idempotencyKey,
      this.comment,
      this.signatureFileId});
  final int expectedRevision, expectedBallotRevision;
  final String choiceKey, idempotencyKey;
  final Map<String, dynamic> values;
  final String? comment, signatureFileId;
  Map<String, dynamic> toJson() {
    if (expectedRevision < 1 ||
        expectedBallotRevision < 1 ||
        !{'yes', 'no', 'abstain'}.contains(choiceKey) ||
        idempotencyKey.trim().isEmpty ||
        idempotencyKey.length > 128 ||
        (comment?.length ?? 0) > 512 ||
        signatureFileId != null &&
            !RegExp(r'^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$',
                    caseSensitive: false)
                .hasMatch(signatureFileId!)) {
      throw const FormatException('Invalid vote request');
    }
    return {
      'expectedRevision': expectedRevision,
      'expectedBallotRevision': expectedBallotRevision,
      'choiceKey': choiceKey,
      'values': values,
      'idempotencyKey': idempotencyKey,
      'comment': comment,
      if (signatureFileId != null) 'signatureFileId': signatureFileId
    };
  }
}
