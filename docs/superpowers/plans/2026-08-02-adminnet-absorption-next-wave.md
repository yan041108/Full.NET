# Admin.NET 吸收后续开发执行计划（Cursor 交接版）

> 执行基线：先合入并验证 `cursor-main-review-20260802` 的审查修复。每个 Task 必须独立创建快照、TDD、完成 affected slice 后提交；禁止多个 Cursor 窗口并发写共享 Unit、Integration、迁移号和 Docker 输出。

## 总体顺序

1. Task 0：关闭本次审查修复的合并门禁。
2. Task 1：迁移诊断策略权限码（预期 052，必须现场确认）。
3. Task 2：请求签名认证失败关闭与请求体上限。
4. Task 3：Tasks 8–10 的真实栈验收和状态校准。
5. Task 4：大型模块只做首个批准规格，不直接批量建项目。

Task 0 未完成不得开始 Task 1；Task 1/2 可分窗口开发，但共享构建、Integration、Docker 和迁移必须串行。

## Task 0：关闭主分支审查修复

**快照：** 沿用现有 `cursor-main-review-20260802`；当前工作区已包含该快照后的审查修复，禁止重新建基线或丢弃未提交改动。

**目标：** 在 Docker 可用机器验证已落盘修复，不扩展产品功能。

**执行：**

1. 确认工作区只包含审查记录列出的文件，执行 `git diff --check`。
2. 启动 Docker Desktop，确认 `docker info` 和 `docker ps` 正常。
3. 运行迁移 043 聚焦测试：
   - `Migration043AuditingOutboundCallRecoveryTests.SqlServer_outbound_audit_migration_recovers_indexes_without_dropping_data`
   - `Migration043AuditingOutboundCallRecoveryTests.MySql_outbound_audit_migration_recovers_indexes_without_dropping_data`
4. 运行 `pnpm test:integration:affected -- --snapshot cursor-main-review-20260802 --phase merge`；必须有 SQL Server 和 MySQL 非零发现。
5. 运行 `pnpm test:container-images`、`pnpm test:e2e`。
6. 重跑 Release solution build、Unit、Architecture、Compatibility、Naming、SQL safety、Governance、Skills、OpenAPI 和 Clients。
7. 更新 `docs/verification/cursor-main-review-2026-08-02.md` 的待验证项；只有全部通过后才提交。

**停止条件：** 043 任一提供程序不能保留数据并恢复正确索引形状、affected 发现为零、Docker teardown 有残留，均停止合并并先修复。

## Task 1：诊断策略权限码规范化与兼容迁移

**建议快照：** `settings-diagnostic-permission-normalization-20260802`

**迁移：** 开始时确认两库 `052` 均空闲；若已占用，停止并重新协调，禁止自动抢号。

**目标：** 把旧权限码：

- `settings.diagnostic-policy.read`
- `settings.diagnostic-policy.write`

迁移为：

- `settings.diagnostic_policy.read`
- `settings.diagnostic_policy.write`

**必须先写 RED：**

1. SQL Server/MySQL 恢复测试覆盖 `fn_identity_role_permission.PermissionCode` 的旧值、目标值已存在、重复授权和二次运行。
2. 覆盖 `fn_identity_api_key.PermissionsJson`：只替换 JSON 数组中的精确旧元素，不得对任意字符串做全局替换；覆盖旧新并存、空数组、无关权限和二次运行。
3. Unit/Integration 覆盖旧数据库升级后，角色与 API Key 的有效授权不丢失。
4. Vue/Layui/Parity E2E 断言新权限码；旧值只允许在迁移和兼容映射测试中出现。

**实现文件：**

- `src/Modules/Full.NET.Modules.Settings.Contracts/DiagnosticPolicyManagementContracts.cs`
- `src/Modules/Full.NET.Modules.Settings/SettingsAuthorizationContributor.cs`
- `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/{SqlServer,MySql}/052_IdentityDiagnosticPolicyPermission.sql`
- `contracts/naming/pre-v1-name-map.json`
- `contracts/naming/naming-debt.json`
- Vue、Layui 和 E2E 的诊断策略权限消费点
- 迁移恢复测试、Identity/Settings 授权 Integration、测试矩阵迁移选择器

**验收：** 双库升级/恢复/幂等通过，新权限码端到端通过，旧权限债务条目删除，Naming/SQL safety/Architecture/OpenAPI/Clients/affected merge 全绿。

## Task 2：请求签名认证安全硬化

**建议快照：** `identity-signature-fail-closed-20260802`

**目标：** 任何攻击者可控输入或损坏持久化数据都不得造成未处理异常、无界内存或泄漏密钥材料。

**必须先写 RED：**

1. `SignatureAuthenticationOptions` 增加有上限的 `MaxBodyBytes`，覆盖零值、负值和过大配置；模块启动必须 `ValidateOnStart()`。
2. 已知 Content-Length 超限在读取前拒绝；未知长度流读取到上限后拒绝，不能把完整请求体复制到无界 `MemoryStream`。
3. 成功和失败后请求体位置均恢复，后续 Endpoint 仍可读取。
4. 数据库中的 `KeyHash` 为空、非 Hex、长度错误时返回统一 Unauthorized，并只记录安全错误码与 TraceId，不记录原始 Hash、签名、Authorization 或正文。
5. 重复签名头、超长 nonce/access-key-id、极端时间戳和取消路径均稳定失败关闭。

**主要文件：**

- `src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationOptions.cs`
- `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthenticationServiceCollectionExtensions.cs`
- `src/Modules/Full.NET.Modules.Identity/Security/SignatureCanonicalRequest.cs`
- `src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationService.cs`
- Identity Unit/Integration、请求签名设计规格和运维配置文档

**验收：** 不新增迁移；Unit、双库 Signature Integration、rate-limit/trusted-proxy、OpenAPI、日志脱敏、affected merge 全绿。

## Task 3：Tasks 8–10 真实栈验收

**建议快照：** `adminnet-tasks-8-10-realstack-20260802`

**目标：** 补证据，不通过扩大功能来“凑 Verified”。

**执行顺序：**

1. Task 8：真实 API Host + SQL Server/MySQL，覆盖代理前缀、空/非空正文、并发重放、密钥轮换/禁用、租户不匹配和失败审计；明确 OpenAccess 产品化仍不在本次范围。
2. Task 9：运行 043 恢复、保留期清理、分页边界、脱敏和 Vue/Layui 查询流程；多副本清理若没有所有权证据，保持 `Build-verified`。
3. Task 10：真实 Host API 验证目录权限拒绝、只读快照、双端渲染；扫描发布产物确认没有 Roslyn/动态 ApplicationPart 路径。
4. 只依据新鲜证据更新 `docs/roadmap/capability-status.md`、`docs/roadmap/adminnet-feature-parity.md` 和总吸收计划。测试数量只更新 `eng/testing/test-matrix.json`。

**验收：** 两库、两管理端、权限拒绝和恢复路径均有非零发现；未满足生产边界的能力继续保持 `Build-verified`。**2026-08-02 已完成验收并记录于 [`docs/verification/adminnet-tasks-8-10-realstack-2026-08-02.md`](../verification/adminnet-tasks-8-10-realstack-2026-08-02.md)。**

## Task 4：大型模块 Gate G4 规格化

**建议快照：** `adminnet-document-spec-20260802`

**目标：** 从既有大型模块队列选择 Document 作为第一个候选，只产出并评审 dated Spec；未批准前不创建空模块、迁移、Endpoint 或通用 Repository。

**规格至少冻结：**

- Files Provider 契约依赖，禁止直连 Files 表；
- 分类、标签、版本、共享、权限、审计、删除/恢复和保留语义；
- Host/租户边界、组织数据范围、逻辑删除与唯一约束；
- SQL Server/MySQL 索引与容量模型；
- Vue/Layui 同步交付和真实栈 E2E；
- 许可、对象存储成本、病毒扫描/内容安全以及退出条件。

**验收：** Spec 获得明确批准并把能力从 `Mapped` 提升到 `Planned`；否则保持 `Mapped`，不进入实现。

## 每个 Task 的固定交付门禁

1. 读取根规则、命名/注释/前端规则和 `fullnet-module-delivery`。
2. 记录 `git rev-parse HEAD`，创建独立 task snapshot。
3. 先 RED 后 GREEN，保留失败证据摘要。
4. 运行 affected inner；切片完成运行 affected slice，合并候选运行 merge。
5. 两库迁移必须成对、可恢复、可幂等且保留数据；迁移号必须现场确认。
6. Vue/Layui 权限、错误处理、关键流程和 E2E 同步。
7. 运行 `git diff --check`、Naming、SQL safety、Governance、Skills、Architecture、OpenAPI、Clients 和相关 Release build。
8. teardown 后确认 shared runner、SQL Server、MySQL、Ryuk/Testcontainers 残留均为 0。
9. 单 Task 单提交；提交信息说明能力和边界。未验证项目必须明确写“未执行/阻断”，不得写“通过”。
