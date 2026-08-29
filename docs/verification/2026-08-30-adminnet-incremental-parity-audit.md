# Admin.NET.Pro 28 提交增量对标审计

- 日期：2026-08-30
- Full.NET 基线：`007de9aa91dd6a31788b12a829d07b63aeab1e7a`
- Admin.NET.Pro 分支：`v2.1`
- 旧基线：`3879b035791b4603e734c15e7c316e0aeca32f1b`（2026-07-13）
- 新基线：`3c65392d8e9c543411b9469a400fe4deee86dc15`（2026-08-07）
- 审计范围：`3879b035..3c65392d`，共 28 个提交、123 个变更文件

## 结论

本增量没有改变 Full.NET 的大型模块执行顺序。Workflow 仍是 Document 之后的下一大型模块，但规格仍为 pending review，状态保持 `Mapped`。

本轮确认三个待补能力：

1. Identity 用户档案的手机号、邮箱、证件号码需要服务端权威格式校验与按作用域唯一性约束；Vue 当前只有手机号长度和邮箱格式的部分校验。
2. Observability Admin 缺少受控的日志文件目录、尾部预览和下载控制面；不得把任意文件系统路径暴露给 API。
3. Notifications 缺少可扩展消息元数据；Admin.NET 的 `object Extras` 不能直接复制，Full.NET 只能在出现真实消费者后使用有界、版本化、可源生成的具体契约。

以下增量能力已被 Full.NET 覆盖，不重复立项：登录失败锁定、分层健康检查、缓存失效一致性、机构写入数据范围。依赖升级、Furion/EF Core/SqlSugar 修复和第二套前端维护不构成 Full.NET 功能缺口。

## 审计方法

1. 使用 Git 固定提交范围和文件清单，不以 README 宣传文本代替源码。
2. 按行为而非 Admin.NET 的 Furion、SqlSugar、EF Core 或前端目录结构映射。
3. 对照 Full.NET 生产源码、Architecture/Integration 测试与 `docs/verification/` 记录。
4. 每组变化归为 `Covered`、`Gap`、`Deferred/Compatibility` 或 `No status change`。
5. 静态审计不产生新的运行证据，因此不把任何能力升级为 `Verified`。

## 逐提交审计

| 提交 | 日期 | Admin.NET.Pro 变化 | Full.NET 处置 |
| --- | --- | --- | --- |
| `c665c1ea` | 2026-07-16 | 格式化与依赖升级 | `No status change`：依赖实现差异，不形成产品能力。 |
| `a0f297ec` | 2026-07-17 | 日志文件预览、登录错误次数限制、日志文件去重 | `Covered + Gap`：登录锁定已覆盖；日志文件控制面进入 Observability Admin 缺口。 |
| `4410859e` | 2026-07-17 | 管理员角色种子与用户编辑优化 | `Covered`：受保护超级管理员、权限目录和用户编辑已有独立边界；不复制种子角色耦合。 |
| `848ab764` | 2026-07-17 | 字典标签、页签刷新、用户编辑失败交互修复 | `No status change`：Full.NET Vue 使用独立实现；后续页面回归按自身测试维护。 |
| `6dfc637e` | 2026-07-18 | 合并提交 | `No status change`：不重复计算其父提交能力。 |
| `bb5206b9` | 2026-07-18 | Web_Artd 同步 | `Deferred`：Full.NET 不维护第二套活动后台产品线。 |
| `5c67df67` | 2026-07-18 | Web_Artd 优化 | `Deferred`：同上。 |
| `920a759d` | 2026-07-20 | 依赖升级 | `No status change`。 |
| `b68c4368` | 2026-07-20 | EF Core 发布冲突修复、消息 `Extras` | `Gap`：发布冲突不适用；消息扩展元数据需强类型、版本化设计，禁止复制 `object`。 |
| `fafadedb` | 2026-07-21 | TypeScript 全局类型修复 | `No status change`。 |
| `deb1fc6f` | 2026-07-21 | TypeScript 全局类型修复 | `No status change`。 |
| `1681241b` | 2026-07-21 | 系统配置布局刷新修复 | `No status change`：Full.NET Settings/Vue 使用独立状态模型。 |
| `820ee175` | 2026-07-23 | 微信 Token 到期调整与前端同步 | `Deferred/Provider`：WeChat 仍为 `Mapped`，没有真实 Provider 需求前不进入 Core。 |
| `93794ab5` | 2026-07-26 | PostgreSQL 备份与下载修复 | `Deferred/Compatibility`：Full.NET 正式数据库为 SQL Server/MySQL，数据库备份属于受控运维面。 |
| `3ad43b44` | 2026-07-27 | 用户页面、枚举颜色、微信客户端生成 | `No status change`：枚举显示和用户页面已有独立实现；WeChat 仍为 `Mapped`。 |
| `630a08f0` | 2026-07-27 | PostgreSQL 修复合并 | `No status change`。 |
| `5acb3eac` | 2026-08-01 | 缓存重构、用户资料校验、日志写入优化 | `Covered + Gap`：缓存治理已覆盖；Identity 服务端权威资料校验列为缺口；日志实现不直接复制。 |
| `88d9e2ca` | 2026-08-02 | Furion 升级 | `No status change`。 |
| `f67e5fe6` | 2026-08-04 | 启动时清接口缓存、实体基类和日志性能调整 | `Covered`：Full.NET 使用静态 Endpoint/AOT 闭包和自有缓存策略，不存在 Furion 动态接口缓存。 |
| `f5c7e014` | 2026-08-04 | Furion 升级 | `No status change`。 |
| `4f2e7bba` | 2026-08-04 | WorkflowCore SQL Server Guid 类型修复 | `No status change`：Workflow 尚未实施；现有 Spec 已要求 UUID v7 双库物理映射。 |
| `a1724a3c` | 2026-08-04 | 读取正在写入的日志文件 | `Gap`：纳入日志文件控制面，首切片必须覆盖并发写入、稳定尾读和下载失败语义。 |
| `fc0ce2eb` | 2026-08-04 | WorkflowCore net10.0 EF Core 冲突 | `No status change`：Full.NET Workflow 计划使用 Dapper，不引入 WorkflowCore EF 持久化。 |
| `6d197e2b` | 2026-08-05 | 健康检查接口、NonUnify 与依赖升级 | `Covered`：Full.NET 已有 live/ready/startup 分层健康检查；兼容包络不进入健康端点。 |
| `5f9aff72` | 2026-08-05 | 合并提交 | `No status change`。 |
| `8217b351` | 2026-08-05 | WorkflowCore MySQL 持久化冲突修复 | `No status change`：强化 Workflow 双库门禁，但不改变 pending-review 状态。 |
| `a5ba3b66` | 2026-08-06 | Furion 与前端依赖升级 | `No status change`。 |
| `3c65392d` | 2026-08-07 | 机构权限从 SuperAdmin/SysAdmin 特判收紧为 SuperAdmin | `Covered`：Full.NET 使用精确 Endpoint 权限、角色数据范围和机构拥有关系写授权，不以普通 SysAdmin 名称绕过。 |

## Full.NET 证据映射

| 能力 | 证据 | 判定 |
| --- | --- | --- |
| 登录失败锁定 | `IdentityUser.FailedLoginCount` / `LockoutEndUtc`、`Login.Handler`、`IdentityOptions.LockoutThreshold` | Covered |
| 分层健康检查 | `HealthEndpointExtensions` 的 `/health/live`、`/health/ready`、`/health/startup` 与 `HealthEndpointTests` | Covered |
| 缓存一致性 | `ICacheInvalidator`、`FusionCacheInvalidator`、`2026-08-29-caching-sdk-boundary.md` | Covered；Caching Admin 控制面仍为 Mapped |
| 机构写授权 | `OrganizationOwnedEntityWriteAuthorizer`、角色数据范围 SQL 投影与双库 Integration | Covered；Admin.NET 变化不改变状态 |
| 用户资料校验 | `UserEditorDialog.vue` 只有手机号长度与邮箱格式部分校验；服务端 ManageHostUsers 未发现手机号/邮箱/证件权威规则和唯一性检查 | Gap |
| 日志文件控制面 | Auditing 现有 API 面向结构化数据库日志，未发现受控文件目录/尾读/下载 Endpoint | Gap；归属 Observability Admin |
| 消息扩展元数据 | `SendHostInboxMessageRequest` 仅 Title/Recipient/Content，实时事件仅传稳定标识和标题 | Gap；需真实消费者与具体契约后实施 |
| Workflow 双库 | `2026-08-20-workflow-module-design.md` 已要求 SQL Server/MySQL 成对迁移、Dapper 和恢复测试 | 设计已覆盖；仍为 Mapped / pending review |

## 状态修正

本次只修正已有证据与路线图文字的漂移：

- Admin.NET.Pro 基线更新为 `3c65392d`。
- 登录/会话、最小 RBAC 和 MemoryPack 可靠事件载荷由笼统的 `Implemented` 对齐为 `Build-verified`；它们已有仓库级测试证据，但本轮不升 `Verified`。
- Document 在全部当前矩阵中保持 `Build-verified`；“队列关闭”不等于 `Verified`。
- SerialNumbers 保持 `Build-verified`，但剩余项修正为“双库 Integration 与真实栈缺少 fresh 全绿”，不再错误声称分页筛选和表单体验尚未实现。
- Workflow 保持 `Mapped（Spec pending review）`。
- Identity 用户资料权威校验、Observability Admin 日志文件控制面和 Notifications 扩展元数据登记为明确后续缺口，不改变现有模块整体 `Build-verified`。

## 下一步顺序

1. Identity 用户资料服务端权威验证纵向切片。
2. B1/B2 已实现能力的双库 real-stack/WCAG fresh 收口。
3. 审查并批准 Workflow Spec 后实施首切片。
4. Observability Admin 日志文件控制面和 Notifications 强类型扩展元数据分别立项；没有真实消费者时不提前创建通用抽象。

## 未验证边界

- 本轮未运行 Admin.NET.Pro，也未验证其 28 个提交的运行正确性；审计只确认源码变化与 Full.NET 处置。
- 本轮不新增 Full.NET 运行行为，因此未运行 SQL Server/MySQL Integration 或浏览器 E2E。
- 依赖升级、安全公告与 Admin.NET.Pro 再分发许可不在本轮范围；未复制其源码或资产。
- 本记录不构成 Production-verified 或 Capacity-verified。
