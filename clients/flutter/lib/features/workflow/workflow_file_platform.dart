import 'package:file_selector/file_selector.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

/// 平台采集只返回有界本地字节；后续上传路径由Todo客户端固定。
Future<({Uint8List bytes, String name, String contentType})?>
    pickWorkflowAttachment() async {
  final file = await openFile();
  if (file == null) return null;
  if (await file.length() > 20 * 1024 * 1024) {
    throw const FormatException('Workflow attachment is too large');
  }
  return (
    bytes: await file.readAsBytes(),
    name: safeWorkflowAttachmentName(file.name),
    contentType: file.mimeType ?? 'application/octet-stream'
  );
}

String safeWorkflowAttachmentName(String name) {
  final safe = name.split(RegExp(r'[/\\]')).last.replaceAll(
      RegExp(r'[\x00-\x1f\x7f<>:"|?*\u202a-\u202e\u2066-\u2069]'), '_');
  return safe.isEmpty
      ? 'attachment'
      : safe.substring(0, safe.length > 128 ? 128 : safe.length);
}

/// 桌面由本人选择保存位置；其他平台仅在应用内显示可识别图片，不打开任意活动内容或写持久缓存。
Future<void> deliverWorkflowAttachment(
    BuildContext context, Uint8List bytes, String name,
    {required bool Function() isCurrent,
    ValueChanged<VoidCallback>? onViewerOpened,
    ValueChanged<VoidCallback>? onViewerClosed}) async {
  if (!isCurrent()) return;
  final safe = safeWorkflowAttachmentName(name);
  if (!kIsWeb &&
      {TargetPlatform.windows, TargetPlatform.linux, TargetPlatform.macOS}
          .contains(defaultTargetPlatform)) {
    final location = await getSaveLocation(suggestedName: safe);
    if (location != null && isCurrent()) {
      await XFile.fromData(bytes, name: safe).saveTo(location.path);
    }
    return;
  }
  final png = bytes.length >= 8 &&
      listEquals(bytes.sublist(0, 8), [137, 80, 78, 71, 13, 10, 26, 10]);
  final jpeg = bytes.length >= 3 &&
      bytes[0] == 255 &&
      bytes[1] == 216 &&
      bytes[2] == 255;
  if (!png && !jpeg) {
    throw UnsupportedError(
        'Workflow file delivery is unavailable for this platform and content type');
  }
  if (!context.mounted || !isCurrent()) return;
  final navigator = Navigator.of(context, rootNavigator: true);
  final visible = ValueNotifier(true);
  final route = DialogRoute<void>(
      context: context,
      builder: (_) => ValueListenableBuilder<bool>(
          valueListenable: visible,
          builder: (_, show, child) =>
              show && isCurrent() ? child! : const SizedBox.shrink(),
          child: AlertDialog(
              title: Text(safe),
              content: InteractiveViewer(
                  child: Image.memory(bytes,
                      cacheWidth: 1200, semanticLabel: safe)))));
  var closed = false;
  void close() {
    if (closed) return;
    closed = true;
    visible.value = false;
    // 身份切换或页面dispose可能发生在导航更新中；先隐藏字节，再移除本次拥有的精确Route。
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (navigator.mounted && route.isActive) navigator.removeRoute(route);
    });
  }

  onViewerOpened?.call(close);
  try {
    if (isCurrent() && !closed) await navigator.push(route);
  } finally {
    onViewerClosed?.call(close);
    visible.dispose();
  }
}
