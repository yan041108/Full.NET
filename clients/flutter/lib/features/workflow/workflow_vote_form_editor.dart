import 'package:flutter/material.dart';
import 'workflow_form_editor.dart';
import 'workflow_vote_models.dart';

/// 独立选票只将自己的字段投影交给共享控件，个人修订与完整答案仍由投票页面处理。
class WorkflowVoteFormEditor extends StatelessWidget {
  const WorkflowVoteFormEditor(
      {super.key,
      required this.form,
      required this.formKey,
      required this.enabled,
      required this.onChanged,
      required this.values,
      this.attachmentNames = const {},
      this.uploadAttachment,
      this.downloadAttachment});
  final WorkflowVoteForm form;
  final GlobalKey<FormState> formKey;
  final bool enabled;
  final ValueChanged<Map<String, dynamic>> onChanged;
  final Map<String, dynamic> values;
  final Map<String, String> attachmentNames;
  final Future<({String fileId, String name})?> Function(String fieldKey)?
      uploadAttachment;
  final Future<void> Function(String fileId, String name)? downloadAttachment;
  @override
  Widget build(BuildContext context) => WorkflowFormEditor(
      form: form.projection.withValues(values, names: attachmentNames),
      formKey: formKey,
      enabled: enabled,
      onChanged: onChanged,
      uploadAttachment: uploadAttachment,
      downloadAttachment: downloadAttachment);
}
