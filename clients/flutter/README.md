# Full.NET Flutter Client

首个业务切片：**工作流待办审批**（登录 → 待办列表 → 同意/驳回）。

## 平台

- 目标平台：Android、iOS、Windows、macOS、Linux（不含 Web；H5 由 uni-app 负责）
- SDK：Flutter **3.44.0**（见 `.flutter-version`）

Windows 工程已入库，需要 Visual Studio 的“使用 C++ 的桌面开发”工作负载与 Windows SDK。其余目标按实际平台生成工程，不需要重新生成 Windows：

```powershell
flutter create --platforms=android,ios,macos,linux --org net.full --project-name fullnet_client .
```

## 配置

```powershell
flutter run --dart-define=FULLNET_API_BASE_URL=https://localhost:5001 --dart-define=FULLNET_LOCALE=zh-CN
flutter build windows --release --dart-define=FULLNET_API_BASE_URL=https://your-api.example
```

访问令牌只保存在内存中，不落本地存储。原生密码登录将配置 API 的 Origin 传给服务端做精确同源校验；Web 的 Origin 由浏览器负责。本地化由 ARB 在正式构建时生成到 `lib/l10n/generated`，手写入口仅保留机器码到译文的严格映射。

## 表单与附件

待办主表单与独立选票共用静态字段控件。可编辑字段按当前策略提交；隐藏、只读及终态字段不进入主表单 Patch。明细限 20 行、16 个标量列，必填单元参与表单校验，草稿递归复制；附件只使用本人当前可见的不透明引用和精确 Todo 上传/内容接口。身份变化或操作返回 401/403/404 时销毁旧投影、草稿及动作回执。

文件选择使用 Flutter 官方 `file_selector` 1.1.0。桌面端由本人选择保存位置，不自动打开文件；Android/iOS 当前只在应用内预览 PNG/JPEG，其他类型明确提示无法打开，仍可按字段约束上传或复用授权引用。没有持久附件缓存。macOS 原生文件对话框需按 [插件说明](https://pub.dev/packages/file_selector)配置用户选择文件的 entitlement；包内控件测试不代表系统对话框或真机验收。

## 验证

```powershell
cd clients/flutter
pnpm test
flutter analyze lib/features/workflow/workflow_form_editor.dart
flutter test --concurrency=1 test/workflow_form_files_test.dart test/workflow_form_files_page_test.dart test/workflow_attachment_test.dart
```

当前仓库 CI 尚未接入 Flutter 构建；`pnpm test` 运行 Node 契约测试，`flutter test` 需在安装 Flutter SDK 后执行。

按修改范围追加模型与直接页面边界测试，不自动运行全量测试。SDK 3.44 的本机定向控件/HTTP 测试与原生平台构建、系统文件对话框、真机结果分开记录，实际证据见 [Workflow 交接清单](../../docs/superpowers/plans/2026-10-28-workflow-cross-machine-handoff.md)。临时编译输出使用独立系统临时目录与正式资源入口；本机已有 `build` 内容保持。
