# Flutter UI Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建面向 Android、iOS、Windows、macOS、Linux 的 Flutter 3.44 客户端 UI 基础，使用 Material 3、Cupertino 和 Full.NET Design Tokens，不依赖第三方整套 UI 框架。

**Architecture:** Flutter SDK 官方组件提供平台 UI，Full.NET 自建轻量 Design System 映射语义令牌并提供自适应 Scaffold/Dialog/Switch。业务功能按 Feature 组织，UI 基础不包含后台管理全量页面，也不承担 H5。

**Tech Stack:** Flutter 3.44.0、Dart SDK 随 Flutter 锁定、Material 3、Cupertino、`flutter_localizations`、Widget/Golden Tests。

## Global Constraints

- `clients/flutter` 不生成 Web 平台；H5 由 uni-app 负责。
- `ThemeData(useMaterial3: true)` 是默认主题，Cupertino 只在需要平台原生行为时由自适应组件选择。
- 不引入第三方整套 UI 框架；新增插件必须覆盖声明平台并通过许可证审查。
- Token、ProblemDetails、BCP 47、多租户和认证语义与其他客户端一致。

---

### Task 1: 创建并锁定 Flutter 多平台工程

**Files:**
- Create: `clients/flutter/.flutter-version`
- Create: `clients/flutter/pubspec.yaml`
- Create: `clients/flutter/analysis_options.yaml`
- Create: `clients/flutter/lib/main.dart`
- Create: `clients/flutter/test/bootstrap_test.dart`
- Modify: `README.md`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: Flutter 3.44.0 stable SDK
- Produces: Android/iOS/Windows/macOS/Linux 工程，不包含 Web

- [ ] **Step 1: 写环境失败检查**

检查 `flutter --version` 必须为 3.44.x，`.flutter-version` 必须为 `3.44.0`，平台目录集合不得包含 `web/`。

- [ ] **Step 2: 创建工程**

Run: `flutter create --platforms=android,ios,windows,macos,linux --org net.full --project-name fullnet_client clients/flutter`

Expected: 五个平台目录生成，未生成 `web/`。

- [ ] **Step 3: 设置 SDK 版本和严格分析规则**
- [ ] **Step 4: 运行分析与 Bootstrap Test**

Run: `cd clients/flutter && flutter analyze && flutter test`

Expected: 0 issue，测试 PASS。

### Task 2: 映射 Full.NET Design Tokens

**Files:**
- Create: `clients/flutter/lib/design_system/full_net_tokens.dart`
- Create: `clients/flutter/lib/design_system/full_net_theme.dart`
- Create: `clients/flutter/lib/design_system/full_net_theme_extension.dart`
- Create: `clients/flutter/test/design_system/full_net_theme_test.dart`
- Create: `scripts/generate-flutter-tokens.mjs`
- Modify: `package.json`

**Interfaces:**
- Consumes: `packages/design-tokens/src/tokens.css`
- Produces: `FullNetTokens`、`FullNetThemeExtension`、light/dark `ThemeData`

- [ ] **Step 1: 写令牌生成和主题失败测试**

断言主色、背景、文字、成功、警告、错误、间距和圆角均有 light/dark 值，业务 Theme 开启 `useMaterial3`。

- [ ] **Step 2: 实现确定性 Token 生成脚本**

脚本只读取语义 CSS Variables，输出稳定排序的 Dart 常量；生成文件带中文说明，禁止手改。

- [ ] **Step 3: 实现 ColorScheme、TextTheme 和 ThemeExtension**
- [ ] **Step 4: 重复运行生成器并验证无 Git 漂移**

Run: `pnpm generate:flutter-tokens && git diff --exit-code clients/flutter/lib/design_system/full_net_tokens.dart`

Expected: 第二次生成无差异。

### Task 3: 官方组件自适应层

**Files:**
- Create: `clients/flutter/lib/design_system/adaptive/full_net_scaffold.dart`
- Create: `clients/flutter/lib/design_system/adaptive/full_net_dialog.dart`
- Create: `clients/flutter/lib/design_system/adaptive/full_net_switch.dart`
- Create: `clients/flutter/lib/design_system/adaptive/full_net_date_time_picker.dart`
- Create: `clients/flutter/test/design_system/adaptive_widgets_test.dart`

**Interfaces:**
- Consumes: Material 3、Cupertino、FullNet Theme
- Produces: 业务页面只依赖的自适应语义组件

- [ ] **Step 1: 为 Android/iOS/Windows 目标写 Widget 失败测试**
- [ ] **Step 2: Android/Windows/Linux 使用 Material 3，iOS/macOS 对明确控件使用 Cupertino**
- [ ] **Step 3: 验证键盘、焦点、Semantics、文字缩放和减弱动画**
- [ ] **Step 4: 运行 Widget Tests**

Run: `cd clients/flutter && flutter test test/design_system`

Expected: 全部 PASS。

### Task 4: 建立应用壳层和多语言入口

**Files:**
- Create: `clients/flutter/lib/app/full_net_app.dart`
- Create: `clients/flutter/lib/app/app_shell.dart`
- Create: `clients/flutter/lib/features/home/home_page.dart`
- Create: `clients/flutter/lib/l10n/app_zh_CN.arb`
- Create: `clients/flutter/lib/l10n/app_en_US.arb`
- Modify: `clients/flutter/pubspec.yaml`
- Test: `clients/flutter/test/app/app_shell_test.dart`

**Interfaces:**
- Consumes: FullNet Theme、`flutter_localizations`、规范 BCP 47 语言
- Produces: 可切换 zh-CN/en-US 的 MaterialApp/Cupertino 自适应壳层

- [ ] **Step 1: 写语言、主题和平台壳层失败测试**
- [ ] **Step 2: 配置 `gen_l10n` 和 ARB，不在 Widget 中硬编码显示文本**
- [ ] **Step 3: 实现窄屏 Bottom Navigation、宽屏 NavigationRail/Drawer 自适应**
- [ ] **Step 4: 运行分析、单测和 Golden Test**

Run: `cd clients/flutter && flutter gen-l10n && flutter analyze && flutter test`

Expected: 全部 PASS。

### Task 5: 平台构建、许可证和状态门禁

**Files:**
- Create: `docs/verification/flutter-ui-foundation.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: 五个平台工程与 UI 测试
- Produces: 按平台记录的构建证据，不做全平台虚假承诺

- [ ] **Step 1: 在 Windows 节点执行 Android 与 Windows 构建**

Run: `cd clients/flutter && flutter build apk --debug && flutter build windows --debug`

Expected: 两个构建成功。

- [ ] **Step 2: 在 macOS 节点执行 iOS/macOS 构建，在 Linux 节点执行 Linux 构建**
- [ ] **Step 3: 运行 `flutter pub deps` 和许可证/平台支持审计**
- [ ] **Step 4: 只把有实际构建证据的平台提升为 `Build-verified`，其余保持 `Designing`**
