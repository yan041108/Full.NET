import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../../l10n/app_localizations.dart';

/// 将固定画布的笔迹编码为受控 PNG，不接受外部图片地址或脚本。
Future<Uint8List> encodeWorkflowSignaturePng(
    List<Offset?> points, double width) async {
  if (!width.isFinite ||
      width <= 0 ||
      width > 4096 ||
      points.whereType<Offset>().length < 2) {
    throw ArgumentError('Invalid signature canvas');
  }
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder)..scale(2);
  _SignaturePainter(points).paint(canvas, Size(width, 160));
  final picture = recorder.endRecording();
  final image = await picture.toImage((width * 2).ceil(), 320);
  try {
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    if (data == null) throw StateError('Signature PNG encoding failed');
    return data.buffer.asUint8List(data.offsetInBytes, data.lengthInBytes);
  } finally {
    image.dispose();
    picture.dispose();
  }
}

/// 签名位图只通过受控采集接口上传，不把临时路径或 URL 当成审批证据。
class WorkflowSignatureCapture extends StatefulWidget {
  const WorkflowSignatureCapture(
      {super.key,
      required this.busy,
      required this.captured,
      required this.onCapture,
      required this.onClear});

  final bool busy;
  final bool captured;
  final Future<void> Function(Uint8List) onCapture;
  final VoidCallback onClear;

  @override
  State<WorkflowSignatureCapture> createState() =>
      _WorkflowSignatureCaptureState();
}

class _WorkflowSignatureCaptureState extends State<WorkflowSignatureCapture> {
  final List<Offset?> _points = [];
  double _width = 300;
  var _encoding = false;
  var _encodingFailed = false;

  void _point(Offset point) {
    if (widget.busy || _encoding) return;
    setState(() => _points.add(Offset(point.dx.clamp(0, _width).toDouble(),
        point.dy.clamp(0, 160).toDouble())));
    widget.onClear();
  }

  Future<void> _save() async {
    if (widget.busy || _encoding || _points.whereType<Offset>().length < 2) {
      return;
    }
    setState(() {
      _encoding = true;
      _encodingFailed = false;
    });
    try {
      final data = await encodeWorkflowSignaturePng(List.of(_points), _width);
      if (mounted) await widget.onCapture(data);
    } catch (_) {
      if (mounted) setState(() => _encodingFailed = true);
    } finally {
      if (mounted) setState(() => _encoding = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final busy = widget.busy || _encoding;
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Text(l10n.signatureTitle),
      const SizedBox(height: 8),
      Semantics(
          label: l10n.signatureCanvas,
          child: LayoutBuilder(builder: (context, constraints) {
            _width = constraints.maxWidth;
            return GestureDetector(
              key: const ValueKey('workflow-signature-canvas'),
              behavior: HitTestBehavior.opaque,
              onPanStart: busy
                  ? null
                  : (event) {
                      _points.add(null);
                      _point(event.localPosition);
                    },
              onPanUpdate: busy ? null : (event) => _point(event.localPosition),
              child: CustomPaint(
                  size: Size(_width, 160),
                  painter: _SignaturePainter(List.of(_points))),
            );
          })),
      Wrap(spacing: 8, children: [
        OutlinedButton(
            onPressed:
                busy || _points.whereType<Offset>().length < 2 ? null : _save,
            child: Text(l10n.signatureSave)),
        TextButton(
            onPressed: busy
                ? null
                : () {
                    setState(_points.clear);
                    widget.onClear();
                  },
            child: Text(l10n.signatureClear)),
      ]),
      Text(widget.captured ? l10n.signatureReady : l10n.signaturePending),
      if (_encodingFailed)
        Semantics(liveRegion: true, child: Text(l10n.actionFailed)),
    ]);
  }
}

class _SignaturePainter extends CustomPainter {
  _SignaturePainter(this.points);
  final List<Offset?> points;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawRect(Offset.zero & size, Paint()..color = Colors.white);
    final ink = Paint()
      ..color = const Color(0xff172033)
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    for (var index = 1; index < points.length; index++) {
      final previous = points[index - 1];
      final current = points[index];
      if (previous != null && current != null) {
        canvas.drawLine(previous, current, ink);
      }
    }
  }

  @override
  bool shouldRepaint(_SignaturePainter oldDelegate) =>
      oldDelegate.points != points;
}
