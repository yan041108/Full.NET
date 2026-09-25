# 执行清单 82 — Flutter 首个明确业务场景 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **82**（Phase C **收官槽**）；`clients/flutter` 首个业务切片 **工作流待办审批**（密码登录 → `/api/v1/workflow/todos/mine` → 详情同意/驳回）；Material 3 + 自有 `full_net_theme`；**不**复制 `ui/admin`、**不**在本槽交付站内信/多模块后台；原生/桌面发布与 `flutter create` 平台工程为部署前置，非本槽 CI 门禁。

## 选定切片

| 项 | 值 |
|----|-----|
| 场景 | 与 uni-app **81** 同域：Workflow 待办（Flutter 侧不含 inbox 页） |
| 平台 | Android / iOS / Windows / macOS / Linux（README）；SDK **3.44.0** |
| 会话 | `IdentitySession`：`/api/v1/auth/login` + `/api/v1/me`；令牌仅内存 |
| 客户端 | `WorkflowTodoClient`：mine / runtime / approve|reject + `createIdempotencyKey` |
| UI | `LoginPage` → `WorkflowTodosPage` → `WorkflowTodoDetailPage` |
| 配置 | `FULLNET_API_BASE_URL` / `FULLNET_LOCALE` dart-define |
| 契约 | `clients/flutter/test/contract.test.mjs`（Node 源码契约，无需 Flutter SDK） |

## 交付锚点

| 层 | 位置 |
|----|------|
| 入口 | `lib/main.dart`；`lib/app/full_net_app.dart` |
| 模型 | `workflow_models.dart` + `workflow_models_test.dart`（需 `flutter test`） |
| 主题 | `design_system/full_net_tokens.dart` |
| 工作区 | `@fullnet/flutter-client` `pnpm test` |

## 清单 82 验收结论

- **已有**：可运行的最小 Flutter 产品壳；与 Host Workflow OpenAPI 路径对齐；权限门与幂等键与 uni-app 契约一致。
- **本槽**：扩展 `contract.test.mjs`（应用范围边界）；`phase-c-82-flutter-workflow-contract.spec.mjs` 触发契约套件；C 区 **48–82** 证据链闭合。
- **未验**：`flutter test` / `flutter build windows`（本机无 Flutter SDK）；应用商店/桌面安装包；清单 **83** 起属 Phase D 延续项（D 区 83–87 已先行 closeout，本槽为 C 区末编号）。

## 停止边界

- **Phase C（48–82）**：至此串行证据槽完成；升 **Verified** 仍受 Gate0、real-stack 与 Gate C 约束。
- 禁止引入 GetWidget 等未批准 UI 栈；禁止将 Token 写入 `shared_preferences` / `secure_storage`。

## 本机验证

| 命令 | 结果 |
|------|------|
| `pnpm test`（`clients/flutter`） | **4/4** 契约用例（`d40de4e6`） |
| `flutter test` / `flutter analyze` | **未执行**（SDK 未安装） |
| `phase-c-82-flutter-workflow-contract.spec.mjs` | 等同 `pnpm test`（Playwright 包装） |
| real-stack / 真机 | **未在本机关闭** |

**状态**：Build-verified；Phase C 程序证据 **closeout 完成**（非 parity Verified）。
