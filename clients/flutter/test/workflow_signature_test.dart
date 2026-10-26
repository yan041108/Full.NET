import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:fullnet_client/features/workflow/workflow_models.dart';
import 'package:fullnet_client/features/workflow/workflow_signature_capture.dart';
import 'package:fullnet_client/l10n/app_localizations.dart';

void main() {
  test('native encoder produces bounded PNG content', () async {
    final bytes = await encodeWorkflowSignaturePng(
            [const Offset(10, 10), const Offset(100, 40)], 300)
        .timeout(const Duration(seconds: 10));
    expect(bytes.take(8).toList(), [137, 80, 78, 71, 13, 10, 26, 10]);
    expect(bytes.length, lessThanOrEqualTo(1048576));
  });
  test(
      'action evidence metadata fails closed and signature identity survives serialization',
      () {
    final value = <String, dynamic>{
      'id': '01912345-6789-7abc-8def-0123456789ab',
      'instanceId': '01912345-6789-7abc-8def-0123456789ac',
      'stepId': '01912345-6789-7abc-8def-0123456789ad',
      'assigneeUserId': '01912345-6789-7abc-8def-0123456789ae',
      'statusKey': 'active',
      'revision': 1,
      'submissionRevision': 2,
      'submission': <String, dynamic>{},
      'allowedActions': ['approve'],
      'activityKindKey': 'human.approval',
      'signRequired': true,
      'commentRequired': true,
    };
    expect(WorkflowTodoDetail.fromJson(value).signRequired, isTrue);
    for (final invalid in [
      {
        ...value,
        'allowedActions': ['approve', 'admin']
      },
      {
        ...value,
        'allowedActions': ['approve', 'approve']
      },
      {...value, 'signRequired': 'true'},
      {...value}..remove('commentRequired'),
    ]) {
      expect(() => WorkflowTodoDetail.fromJson(invalid), throwsFormatException);
    }
    final request = ActWorkflowTodoRequest(
        expectedRevision: 1,
        expectedFormRevision: 2,
        fieldPatch: const {},
        idempotencyKey: 'signed',
        signatureFileId: value['id'] as String);
    expect(request.toJson()['signatureFileId'], value['id']);
  });

  testWidgets(
      'drawing captures real PNG bytes and clear invalidates the captured signature',
      (tester) async {
    Uint8List? capture;
    var cleared = 0;
    await tester.pumpWidget(MaterialApp(
        locale: const Locale('en', 'US'),
        localizationsDelegates: const [AppLocalizations.delegate],
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
            body: WorkflowSignatureCapture(
                busy: false,
                captured: false,
                onCapture: (bytes) async {
                  capture = bytes;
                },
                onClear: () {
                  cleared++;
                }))));
    expect(tester.widget<OutlinedButton>(find.byType(OutlinedButton)).onPressed,
        isNull);
    final gesture = await tester.startGesture(tester
        .getCenter(find.byKey(const ValueKey('workflow-signature-canvas'))));
    await gesture.moveBy(const Offset(30, 10));
    await tester.pump();
    await gesture.moveBy(const Offset(40, 20));
    await tester.pump();
    await gesture.up();
    await tester.pump();
    expect(cleared, greaterThan(0));
    expect(tester.widget<OutlinedButton>(find.byType(OutlinedButton)).onPressed,
        isNotNull);
    await tester.runAsync(() async {
      // 不在真实异步区等待虚拟时钟内创建的 Completer，直接等待本轮原生编码的 Future。
      final save = tester
          .widget<OutlinedButton>(find.byType(OutlinedButton))
          .onPressed! as Future<void> Function();
      await save().timeout(const Duration(seconds: 10));
    });
    expect(capture!.take(8).toList(), [137, 80, 78, 71, 13, 10, 26, 10]);
    await tester.pumpAndSettle();
    final before = cleared;
    await tester.tap(find.text('Clear signature'));
    await tester.pump();
    expect(cleared, before + 1);
    expect(tester.widget<OutlinedButton>(find.byType(OutlinedButton)).onPressed,
        isNull);
  });
}
