# Notifications 平台扩展切片收口（2026-08-31）

- **任务：** P1 统一消息中心 Task 8（平台切片关闭与真实 Provider 后续门禁）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-platform-kernel-20260831`（计划写的 `notifications-platform-extension-20260830` 不存在；收口复用 Task 1 开工快照，覆盖 Task 1–7 全部平台改动）
- **范围：** 汇总 Tenant Inbox、模板/Intent、Profile/Binding、Delivery Worker、Vue 控制面的双库与静态门禁；确认 Test Provider 不进入产品程序集；按 Spec §14 停止真实厂商实现。不含偏好 API、新迁移 106、生产 Adapter 项目、Layui、Linux Native AOT publish。

## 证据

| 命令 | 结果 |
|---|---|
| Architecture `FullyQualifiedName~Notifications` `--minimum-expected-tests 6` | **7/7**（含 Test Provider 不进生产程序集、HostedService 只在 Worker `AddBackgroundServices`） |
| Unit `FullyQualifiedName~Notifications` `--minimum-expected-tests 40` | **46/46** |
| `pnpm test:naming` | **30/30** |
| `pnpm test:integration:affected:plan -- --snapshot notifications-platform-kernel-20260831 --phase slice` | 接管复核时变更文件 155；目标 `Identity, integration-matrix, migration-104, migration-105, Notifications`；预计约 9 分钟。Identity 进入选择器是因为 `AdminNavigationWhitelist.cs` 登记了通知导航，合理。 |
| `pnpm test:slice -- --snapshot notifications-platform-kernel-20260831` | 接管修复后发现 36 项（UID 去重，双 Provider）；**36/36 通过**，6m 52s。工具链 **53/53**、治理 **52/52**、Release 构建 0 警告 0 错误。 |
| `pnpm test:dotnet:architecture -- --selection api-native-aot` | **73/73**（最低发现数 36） |
| `pnpm test:aot:analyzers` | **通过**（Host.Api `FullNetAotAnalysis=true`，0 警告 0 错误） |
| `pnpm test:aot:worker:analyzers` | **通过**（Worker 分析构建 + JIT Rebuild，0 警告 0 错误） |
| `pnpm test:openapi` / `pnpm openapi:client:generate -- --check` | **122/122**；生成产物零漂移，Workflow Definition 5 个 Operation 已进入统一生成客户端。 |
| Vue 全量 / build / bundle / audit | **168/168 文件、591/591 测试**；typecheck 与生产构建通过；四项包体预算 PASS；无未审查 Critical/High 依赖告警。 |
| `pnpm test:aot:publish:linux` / 原生外部进程 E2E | **未跑**。本机 Windows 不得把 Linux Native AOT publish 或本切片新 HTTP/JSON/Dapper 路径标为 `Aot-published`。既有 CI [run 32849677783](https://github.com/yan041108/Full.NET/actions/runs/32849677783) 覆盖平台扩展之前的 Host Inbox/Announcement，不能当作本切片新路径的 Linux 原生证据。 |
| Vue Unit / typecheck / build | Task 7 **Build-verified**，见[Vue 控制面验证](2026-08-31-notifications-vue-control-plane.md) |
| `tests/e2e/admin-real-stack/tests/notification-platform.spec.mjs` | **SQL Server 1/1、MySQL 1/1 通过**：真实创建 inbox 模板、发布并在列表确认“已发布 v1”，同时覆盖既有模板/Profile/Binding/Delivery 控制面。 |

slice 覆盖 SQL Server 与 MySQL 的 104/105 迁移恢复、Notifications API（Host/Tenant Inbox、模板/Intent 幂等、Profile/Binding 空目录与密钥不回显、Delivery 租约/Attempt/Receipt/人工重试）以及因导航白名单进入选择器的 Identity。本轮 Identity 无会话切换/死锁 flake。

### 真实栈补验与模板列表修复

- 以快照 `notifications-platform-realstack-20260831` 继续补验。增强 E2E 前，既有 Notifications 真实栈在 SQL Server、MySQL 各 **1/1** 通过；增强后两库仍各 **1/1** 通过。
- 新增“创建草稿 → 发布 → 列表显示已发布版本”的真实用户链路后，先红后绿发现并修复模板列表未装载最新发布版本摘要的问题。列表改为一次 `LEFT JOIN` 读取独立 `NotificationTemplateListRecord`，不引入逐行补查。
- 双库 Integration 回归验证列表返回 `LatestPublishedVersionId`、版本号 `1`、内容哈希和分类；`pnpm test:slice -- --snapshot notifications-platform-realstack-20260831` **2/2** 通过。
- 新列表读模型已进入 Dapper AOT 物化器与静态投影门禁；Architecture `api-native-aot` **73/73**、Host.Api/Worker AOT 分析均为 0 警告 0 错误。OpenAPI **122/122**、命名 **30/30**、治理 **52/52** 通过。
- Vue 模板列表新增“草稿 / 已发布 vN”状态；聚焦测试 **4/4**、typecheck 与生产构建通过。构建仍报告既有 VForm3 `eval` 与大 chunk 警告，本轮未把它们表述为已关闭。

接管复核还以先红后绿回归关闭了四项遗漏：回执按 `ProviderTypeKey + ProviderMessageId` 联合关联，避免跨 Provider 串单；78 条 Notifications `Global` SQL 全部进入精确安全目录并删除多余兼容别名；Profile 发布改用独立 `notifications.provider_profiles.publish` 权限；Profile 更新的 `SecretReference=null` 改为保留现值，避免普通配置编辑静默清空密钥引用。

完整 Architecture 套件仍有当前分支既有红项（Identity/Tenancy Dapper 依赖扫描、Workflow 既有项目引用预期、Kafka 注册方法旧命名、公共错误码命名和 SerialNumbers 动态 SQL 构造）；Notifications 新增 Global SQL 已从 81 条违规降为 0。上述既有红项不由本切片改动引入，不把完整 Architecture 表述为通过。未跑完整 `pnpm test:sql-safety`（历史 009/011/051/093 豁免行号偏差仍在，未改这些文件）。

## 行为与边界

- 生产 `Providers/` 只有 `INotificationProviderAdapter`、`INotificationReceiptVerifier`、`NotificationProviderTypeCatalog`；目录由已注册 Adapter 构成，生产零 Adapter 时为空。`TestNotificationProvider` 只存在于 `tests/Full.NET.IntegrationTests/Notifications/`。
- HostedService 只在 Worker `AddBackgroundServices` 注册；API `AddServices` 不启动领取循环。
- 外部 I/O 在事务外；Enabled Profile 不自动 FanOut；回执不直接改变业务或 Workflow 状态。以上由 Task 3–6 双库 Integration 与本收口 slice 共同覆盖。
- 未实现偏好 API（Spec 阶段 5，等首个真实 Provider）。未创建生产 Provider 项目或厂商纵向计划。未改 Layui。未新建迁移 106。

## 结论

- 平台扩展切片按当前证据关闭，新扩展最多 **Build-verified**，不得升 `Verified`。
- 真实邮件/短信/企微/公众号/钉钉仍为 **Planned** / 对标矩阵 **Mapped**。容量继续 **`Capacity-not-verified`**。
- 首个真实 Provider 尚未选定。按 Spec §14 与计划停止条件，不开始厂商 SDK/协议/沙箱实现；选定后再另建独立纵向计划。
- 本任务未触发规则或 Skill 演进。
