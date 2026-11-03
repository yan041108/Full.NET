import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fullnet_client/features/workflow/workflow_form_projection.dart';
import 'package:fullnet_client/features/workflow/workflow_form_editor.dart';
import 'package:fullnet_client/features/workflow/workflow_file_platform.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';

const id = '01912345-6789-7abc-8def-0123456789ab';
Map<String, dynamic> schema() => {
      'schemaVersion': 1,
      'adapterVersion': 1,
      'sections': [
        {
          'sectionKey': 'main',
          'fields': [
            {
              'fieldKey': 'input',
              'fieldTypeKey': 'attachment',
              'required': false,
              'constraints': {'maxCount': 2}
            },
            {
              'fieldKey': 'output',
              'fieldTypeKey': 'attachment',
              'required': false,
              'constraints': {'maxCount': 2}
            },
            {
              'fieldKey': 'rows',
              'fieldTypeKey': 'subtable',
              'required': true,
              'constraints': {
                'maxRows': 2,
                'columns': [
                  {
                    'columnKey': 'title',
                    'fieldTypeKey': 'text',
                    'required': true,
                    'constraints': {'maxLength': 32}
                  },
                  {
                    'columnKey': 'tags',
                    'fieldTypeKey': 'checkbox',
                    'required': false,
                    'constraints': {
                      'options': ['one', 'two']
                    }
                  }
                ]
              }
            }
          ]
        }
      ]
    };
WorkflowFormProjection form(
        [Map<String, dynamic> values = const {},
        Map<String, String> policies = const {}]) =>
    WorkflowFormProjection.parse(schema(), values, policies, readOnly: false);
Widget app(Widget child) => MaterialApp(
    locale: const Locale('en', 'US'),
    supportedLocales: AppLocalizations.supportedLocales,
    localizationsDelegates: const [AppLocalizations.delegate],
    home: Scaffold(body: SingleChildScrollView(child: child)));

void main() {
  test('中间无效输入可标记草稿但不能生成Patch，重建不替换权威基线', () {
    final source = WorkflowFormProjection.parse({
      'schemaVersion': 1,
      'adapterVersion': 1,
      'sections': [
        {
          'sectionKey': 'main',
          'fields': [
            {
              'fieldKey': 'count',
              'fieldTypeKey': 'integer',
              'required': false,
              'constraints': <String, dynamic>{}
            }
          ]
        }
      ]
    }, <String, dynamic>{
      'count': 1
    }, <String, dynamic>{}, readOnly: false);
    final draft = <String, dynamic>{'count': '-'};
    expect(source.hasChanges(draft), true);
    expect(source.withValues(draft).values['count'], '-');
    expect(source.values['count'], 1);
    expect(() => source.patch(draft), throwsFormatException);
  });
  test('附件及明细拒绝外部URL、重复引用、未知列和无界行', () {
    for (final values in [
      {
        'input': ['https://example.test/file']
      },
      {
        'input': [id, id]
      },
      {
        'rows': [
          {'unknown': 'value'}
        ]
      },
      {
        'rows': [{}, {}, {}]
      },
      {
        'rows': ['bad']
      }
    ]) {
      expect(() => form(values), throwsFormatException);
    }
    expect(
        () => form({
              'input': [id]
            }),
        returnsNormally);
  });
  test('共享投影与Patch深复制，不提交隐藏、只读或未知字段', () {
    final source = {
      'rows': [
        {
          'title': 'old',
          'tags': ['one']
        }
      ],
      'input': [id]
    };
    final projection = form(source, {'input': 'readOnly'});
    (source['rows'] as List).clear();
    expect(projection.values['rows'], hasLength(1));
    final candidate = cloneWorkflowValues(projection.values);
    (candidate['rows'] as List).first['title'] = 'new';
    candidate['input'] = [];
    final patch = projection.patch(candidate);
    expect(patch.keys, ['rows']);
    ((patch['rows'] as List).first['tags'] as List).clear();
    expect((projection.values['rows'] as List).first['tags'], ['one']);
    expect(() => projection.patch({...candidate, 'foreign': 1}),
        throwsFormatException);
  });
  test('安全名称不包含路径、控制符或双向显示控制，不使用引用Id作为名称', () {
    expect(safeWorkflowAttachmentName('../folder/report\n.png'), 'report_.png');
    expect(safeWorkflowAttachmentName(''), 'attachment');
    expect(safeWorkflowAttachmentName('\u202esecret.txt'), '_secret.txt');
  });
  testWidgets('明细单元参与同一Form校验，可复用可见附件并保留安全名称', (tester) async {
    final key = GlobalKey<FormState>();
    Map<String, dynamic>? changed;
    final projection = WorkflowFormProjection.parse(
        schema(),
        {
          'input': [id],
          'rows': [<String, dynamic>{}]
        },
        {'input': 'readOnly'},
        readOnly: false,
        attachmentNames: {id: 'source-owned.png'});
    await tester.pumpWidget(app(WorkflowFormEditor(
        form: projection,
        formKey: key,
        enabled: true,
        attachmentCandidates: {id: 'source-owned.png'},
        onChanged: (value) => changed = value)));
    expect(key.currentState!.validate(), false);
    await tester.pump();
    await tester.enterText(
        find.widgetWithText(TextFormField, 'title *'), 'result');
    await tester.pump();
    expect(key.currentState!.validate(), true);
    await tester
        .ensureVisible(find.text('Reuse visible attachment: source-owned.png'));
    await tester.tap(find.text('Reuse visible attachment: source-owned.png'));
    await tester.pump();
    expect(changed!['output'], [id]);
    expect(changed!['rows'], [
      {'title': 'result'}
    ]);
    expect(find.text(id), findsNothing);
  });
  testWidgets('只读终态和隐藏策略关闭编辑与上传入口', (tester) async {
    final projection = WorkflowFormProjection.parse(
        schema(), <String, dynamic>{}, <String, dynamic>{'input': 'hidden'},
        readOnly: true);
    await tester.pumpWidget(app(WorkflowFormEditor(
        form: projection,
        enabled: true,
        uploadAttachment: (_) async => (fileId: id, name: 'file.png'),
        onChanged: (_) => fail('read-only mutation'))));
    expect(find.text('input'), findsNothing);
    expect(find.text('Upload attachment'), findsNothing);
    expect(find.text('Add row'), findsNothing);
  });
}
