# 架构复核缺口后续实施计划

> **交付对象：Cursor。** 按 Task 顺序逐项执行；一次只开一个 Task，不并行占用共享 `.NET` 输出、Integration/Docker 或迁移号。每项行为变更必须先 RED，再做最小 GREEN。模块纵向切片必须使用项目 Skill `$fullnet-module-delivery`。

**Goal:** 关闭 2026-08-08 架构复核确认的真实缺口：供应链失败关闭、文档编码门禁、缓存策略 allowlist、5 个跨模块本地事务债务、首个完整消费方投影范例、Document→Files claim 状态机，以及 Vue/OpenAPI/共享契约覆盖。

**Architecture:** 保持强化型模块化单体和同进程 Contract Adapter，不提前网络化。每个模块只事务写入自己的表；同步 Port 只提供调用时点答案，高频外部事实进入消费方投影，跨模块资源引用使用 claim/reconcile，重要业务事件通过本模块事务 Outbox。SQL Server/MySQL 继续同等支持。

**Baseline:** Cursor 开工时必须重新读取 `git rev-parse HEAD`，不得假定本文编写时的 `bbb718a4` 仍是最新基线。

## 全局执行协议

1. 每个 Task 开始前确认 `git status --short --branch`，创建本文指定 snapshot；工作区不干净时不得把既有改动纳入当前影响集。
2. 任何双库迁移只在该 Task 真正开始时重新扫描两库现有最大编号，成对预留下一个空闲号；禁止根据本文日期预占或覆盖编号。
3. 每个 Task 内循环执行：聚焦 RED → 最小 GREEN → `pnpm test:integration:affected:plan -- --snapshot <id> --phase inner`。
4. 切片关闭执行：相关 Unit/Architecture/Node 门禁、`pnpm test:integration:affected -- --snapshot <id> --phase slice`、`git diff --check`；用到 Docker 时等待 Ryuk 自然退出并报告 `runner=0`、数据库/Testcontainers residual=0。
5. 不更新文档中的测试数量；数量只由新鲜 discovery 更新 [`eng/testing/test-matrix.json`](../../../eng/testing/test-matrix.json)。
6. Task 4–6 每次只允许一个窗口修改模块生产代码、模块 Contracts、Composition、迁移和对应 Unit/Integration；不得交叉并行。
7. Layui 保持冻结，所有客户端工作只修改 Vue 主线 `ui/admin` 和共享契约。

## 执行队列

| 顺序 | Snapshot | Task | 优先级 | 结束门槛 |
|---:|---|---|---|---|
| 1 | `architecture-doc-integrity-20260808` | 权威 Markdown UTF-8 完整性门禁 | P0 | 损坏 fixture RED，仓库文档 GREEN，治理聚合通过 |
| 2 | `architecture-nuget-audit-gate-20260808` | NuGet Critical/High 失败关闭 | P0 | 纯策略测试、真实 restore/audit、CI 契约通过 |
| 3 | `architecture-cache-policy-zero-allowlist-20260808` | 缓存策略 allowlist 收敛为零 | P1 | Tenancy/Settings Unit、Architecture、affected 通过 |
| 4 | `architecture-local-tx-medium-debt-20260808` | 退役 3 项 medium 本地事务债务 | P1 | 债务目录仅剩 2 项 high，相关双库行为通过 |
| 5 | `architecture-identity-org-projection-20260808` | Identity 消费 Organization 单位投影 | P1 | 回填/重建/乱序/对账/双库通过，high 债务减 1 |
| 6 | `architecture-document-files-claim-20260808` | Document→Files 引用 claim 状态机 | P1 | commit-unknown/重复/对账/删除竞态双库通过，债务目录清零 |
| 7 | `architecture-vue-contract-coverage-20260808` | Vue/OpenAPI/共享 TS 契约覆盖门禁 | P2 | 所有生产 Vue API 文件被精确覆盖，无宽泛豁免 |
| 8 | 不创建 | 首个真实事件 v1→v2 演练 | Decision Gate | 只有真实非加法事件获批后另建 Spec/计划 |

---

## Task 1：权威 Markdown UTF-8 完整性门禁

**Files**

- Create: `scripts/governance/validate-authoritative-markdown.mjs`
- Create: `tests/governance/authoritative-markdown-encoding.test.mjs`
- Modify only if required: `package.json`
- Create after GREEN: `docs/verification/authoritative-markdown-integrity-2026-08-08.md`

**Required behavior**

- 扫描 `AGENTS.md`、`rules/**/*.md`、`.agents/skills/**/*.md`、`docs/architecture/**/*.md`、`docs/roadmap/**/*.md`、`docs/superpowers/specs/**/*.md`、`docs/superpowers/plans/**/*.md` 与 `docs/operations/**/*.md`。
- 使用 `TextDecoder('utf-8', { fatal: true })` 验证字节；拒绝 UTF-16 BOM、无效 UTF-8 和实际 `U+FFFD`。
- 排除 fenced code、行内 code 和 URL 后，拒绝中文权威文档正文中的连续 ASCII `?` 乱码；至少覆盖 `???` 以上的替换串，同时保留代码中的空值运算符、查询字符串和正常中文问号 `？`。
- 错误必须列出仓库相对路径和首个字符/字节位置；不得静默修复或改写文件。
- 测试使用临时 fixture 覆盖有效 UTF-8、截断多字节、UTF-16、显式 `U+FFFD`、中文被 ASCII 问号批量替换，以及代码围栏/行内代码中的合法问号；禁止为了让当前仓库通过而建立路径 allowlist。

**RED/GREEN**

```powershell
pnpm test:task:start -- architecture-doc-integrity-20260808
node --test tests/governance/authoritative-markdown-encoding.test.mjs
pnpm test:governance
```

先让损坏 fixture 被当前缺失实现准确拒绝，再实现扫描器。最终两条命令必须非零发现且通过。

---

## Task 2：NuGet Critical/High 失败关闭

**Files**

- Create: `security/dotnet-audit-policy.json`
- Create: `scripts/audit-dotnet-dependencies.mjs`
- Create: `tests/dotnet-audit-policy.test.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`
- Align exact exception metadata: `security/client-audit-policy.json`
- Modify if schema alignment requires: `scripts/audit-client-dependencies.mjs`
- Modify: `tests/client-audit-policy.test.mjs`
- Create after GREEN: `docs/verification/dependency-vulnerability-gates-2026-08-08.md`

**Policy contract**

```json
{
  "schemaVersion": 1,
  "minimumSeverity": "high",
  "exceptions": [
    {
      "advisory": "GHSA-or-CVE",
      "package": "Exact.Package.Id",
      "allowedPaths": ["exact>dependency>path"],
      "rationale": "non-empty",
      "mitigations": ["non-empty"],
      "owner": "named team or role",
      "reviewBy": "YYYY-MM-DD",
      "expiresOn": "YYYY-MM-DD"
    }
  ]
}
```

**Required behavior**

1. `audit-dotnet-dependencies.mjs` 调用支持 JSON 的 `dotnet list Full.NET.slnx package --vulnerable --include-transitive --format json`，导出纯函数 evaluator 供 fixture 测试。
2. 任意 Critical 永不允许例外；High 默认失败。例外必须同时精确匹配 advisory、包和每一条实际依赖路径，过期、缺少 owner/mitigation、通配符、额外路径均失败。
3. dotnet 命令失败、restore 失败、JSON 缺字段或无法解析必须失败关闭；不能把“没有可解析 finding”当成安全。
4. `package.json` 增加 `audit:dotnet`；CI 用它替换当前只列表的 NuGet 命令，并保留 `audit:clients`。
5. npm 例外补齐同一套 `owner/reviewBy/expiresOn` 元数据，保持既有精确路径与 Critical 永不放行语义。

**RED/GREEN**

```powershell
pnpm test:task:start -- architecture-nuget-audit-gate-20260808
node --test tests/dotnet-audit-policy.test.mjs tests/client-audit-policy.test.mjs
pnpm audit:dotnet
pnpm audit:clients
```

测试至少覆盖：零 finding、Critical、未审 High、精确例外、路径漂移、过期例外、Malformed JSON、命令失败。若真实扫描发现漏洞，不得先写宽泛例外转绿；优先升级，确实无法升级时提交精确、有到期日的风险审查。

---

## Task 3：缓存策略 allowlist 收敛为零

**Files**

- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ICachePolicyRegistry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/CachePolicyRegistry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheEntryNames.cs`
- Add if needed: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheEntryLifetime.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Features/ManageMyGridPreferences/MyGridPreferenceService.cs`
- Modify: `tests/Full.NET.UnitTests/Caching/CachePolicyRegistryTests.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/TenantResolverTests.cs`
- Modify: `tests/Full.NET.UnitTests/Settings/GridPreferenceTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/CachePolicyBoundaryTests.cs`
- Create after GREEN: `docs/verification/cache-policy-zero-allowlist-2026-08-08.md`

**Design**

- 在注册表增加稳定条目 `settings.grid-preference`，归属 Settings，分类 S2，L1 15 分钟、L2 7 天、Fail-Safe 关闭；Tenant Resolution 保持 S1、正常 5 分钟、负缓存 1 分钟。
- `ICachePolicyRegistry` 必须能生成 `HybridCacheEntryOptions`，并显式区分正常与负缓存 lifetime；业务模块不得读取策略后再次手写 TTL。
- C0/N0 必须拒绝创建缓存选项；S0-L2 必须映射为禁用本地缓存；未知条目失败关闭。
- `TenantResolver` 与 `MyGridPreferenceService` 注入注册表并删除所有 `new HybridCacheEntryOptions`。
- `CachePolicyBoundaryTests.AllowedSites` 最终为空，测试明确断言零 allowlist；不得把允许数从 2 改成另一个非零常量。

**RED/GREEN**

```powershell
pnpm test:task:start -- architecture-cache-policy-zero-allowlist-20260808
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CachePolicyRegistryTests|FullyQualifiedName~TenantResolverTests|FullyQualifiedName~GridPreferenceTests"
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~CachePolicyBoundaryTests"
pnpm test:integration:affected -- --snapshot architecture-cache-policy-zero-allowlist-20260808 --phase slice
```

不得重新引入缓存失效 Outbox；旧 Handler 的删除仍受“所有环境历史消息已排空”独立门禁约束，本 Task 不删除兼容 Handler。

---

## Task 4：退役 3 项 medium 跨模块本地事务债务

**Files**

- Modify: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/TenantUserUnitManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserPositions/TenantUserPositionManagementService.cs`
- Modify: `tests/Full.NET.UnitTests/Notifications/HostInboxMessageServiceTests.cs`
- Add focused Organization Unit tests beside existing Organization Unit tests
- Modify: `contracts/architecture/module-local-transaction-debt.json`
- Create after GREEN: `docs/verification/module-local-transaction-medium-debt-retirement-2026-08-08.md`

**Design and invariants**

1. Notifications 在进入本地事务前完成内容校验和收件人 active 查询；事务内只写 Notifications 表和同事务 Outbox。收件人在校验后停用只会形成不可展示的历史消息，不得扩大权限；登录/会话和查询仍按 Identity 权威状态失败关闭。
2. Organization 的两条创建入口在事务前读取 active Host 用户；事务内只校验/写 Organization 自有单位、职位和隶属表。用户随后停用时，Identity 权威状态阻止认证，Organization 隶属不是授权真源。
3. 三个服务都必须有调用顺序测试：Contract 异常时事务未开始；Contract 成功后事务恰好开始一次；写入失败仍回滚。
4. 删除且只删除这 3 条精确 debt；Identity→Organization 和 Document→Files 两项 high 继续保留，禁止为“清零”提前删除。

**RED/GREEN**

```powershell
pnpm test:task:start -- architecture-local-tx-medium-debt-20260808
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HostInboxMessageServiceTests|FullyQualifiedName~TenantUserUnit|FullyQualifiedName~TenantUserPosition"
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~ModuleLocalTransactionBoundaryTests"
pnpm test:integration:affected -- --snapshot architecture-local-tx-medium-debt-20260808 --phase slice
```

---

## Task 5：Identity 消费 Organization 单位投影

**Files**

- Create: `src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationUnitIntegrationEvents.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/TenantUnitManagementService.cs`
- Create under Identity: `Features/OrganizationUnitProjection/**`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleDataScopeService.cs`
- Modify: Organization/Identity module registration and Worker handler registration in Composition
- Add paired SQL Server/MySQL migration using the next free number at Task start
- Add Unit: event serialization, idempotency, old-version rejection, handler retry, backfill and reconciliation
- Add Integration: both providers for migration recovery, CRUD→Outbox→projection, rebuild and role data-scope validation
- Modify: `contracts/architecture/module-local-transaction-debt.json`
- Create after GREEN: `docs/verification/identity-organization-unit-projection-2026-08-08.md`

**Event contract**

```csharp
[MessagePackObject]
public sealed record OrganizationUnitChangedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] Guid UnitId,
    [property: Key(2)] string Name,
    [property: Key(3)] bool IsActive,
    [property: Key(4)] long Version,
    [property: Key(5)] DateTimeOffset ChangedAtUtc);
```

已发布 Key 只能尾部追加。消息类型使用稳定点分层常量，Organization 在创建、更新和禁用业务数据的同一本地事务中写 Outbox。

**Projection design**

- Identity 自有表建议为 `fn_identity_organization_unit_projection`，至少包含 `TenantId, UnitId, Name, IsActive, SourceVersion, SourceUpdatedAtUtc, ProjectedAtUtc`，业务唯一键 `(TenantId, UnitId)`。
- Handler 在 Identity 本地事务内按 `(TenantId, UnitId)` 幂等 upsert，只有 `incoming.Version > SourceVersion` 才更新；重复与乱序消息成功 no-op。
- 回填通过 Organization 批量 Contract 分页，不读 Organization 表；支持全量重建、断点续跑和 dry-run 差异对账。
- 切换顺序固定为 expand→事件发布→backfill→dual-check→role data-scope 改读 Identity 投影→移除同步 Port 事务依赖→删除精确 debt。投影未完整前不得 cutover。
- 自定义数据范围只接受同一 Tenant 的 active 单位；旧投影、缺失投影和停用单位必须失败关闭。

**Required tests**

- Organization 事务失败时没有 Outbox；成功时业务状态与事件一起提交。
- 相同消息重放、旧版本晚到、新版本先到、处理进程中断、backfill 重跑均保持正确。
- 事件消费不得逐条回调 Organization。
- SQL Server/MySQL 均验证投影恢复、索引和 role update；Architecture debt 最终只剩 Document→Files 一项 high。

---

## Task 6：Document→Files 引用 claim 状态机

**Files**

- Create/modify in `src/Modules/Full.NET.Modules.Files.Contracts/`: claim command/result and reconciliation probe contracts
- Create under `src/Modules/Full.NET.Modules.Files/Features/HostFileReferenceClaims/**`
- Modify Files deletion/retention path so Pending/Active claim 均阻止 Blob/记录删除
- Modify: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemManagementService.cs`
- Create Document exact reference probe under `Features/HostFileReferences/**`
- Modify Files/Document module registration and Worker reconciliation registration
- Add paired SQL Server/MySQL migration using the next free number at Task start
- Add Unit and Integration for both providers
- Modify: `contracts/architecture/module-local-transaction-debt.json`
- Create after GREEN: `docs/verification/document-files-reference-claim-2026-08-08.md`

**State model**

```text
Pending --Document commit observed--> Active
Pending --known rollback / aged reconciliation proves absent--> Released
Active  --consumer reference is permanently removed--> Released
```

**Required invariants**

1. Document 在开始本地事务前以稳定幂等键 `document-version:<VersionId>` 向 Files 创建 Pending claim；Files 在自己的事务中验证文件 Ready 并写 claim。
2. Document 只在本地事务中写 Document 表。已知业务失败/已知回滚可以释放 claim；提交结果未知、取消发生在 commit 阶段或确认调用失败时**不得删除 Blob、不得释放 claim**，留给对账。
3. Document 提交后幂等 Confirm；重复 Claim/Confirm/Release 必须返回同一语义，payload 冲突失败关闭。
4. Files reconciler 批量领取超龄 Pending claim，通过注册的 Document probe 检查精确 `VersionId + FileId`：存在则 Active，不存在且超过安全宽限期才 Released。探测失败保持 Pending 并计量，不猜测释放。
5. Files 删除必须在同一 Files 本地事务内拒绝任何 Pending/Active claim；Document 的精确 probe 和现有 retention contributor 作为迁移期双保险，不能在同一切片先删旧保护。
6. 完成 expand→双写/对账→cutover 后，`AddVersionCoreAsync` 不再在 Document 事务内调用 `IHostFileReferenceReader`，最后一条 debt 才可删除。

**Required failure matrix**

- claim 成功但 Document 事务已知回滚；
- Document commit 成功但 Confirm 失败；
- commit 返回结果未知，真实数据库最终分别为已提交/未提交；
- worker 与在线 Confirm 竞争；
- 重复请求、幂等键 payload 冲突、超龄 claim、probe 超时；
- 文件删除与 Claim 并发；
- SQL Server/MySQL 迁移半完成、重跑、索引畸形恢复。

不得把 Files 和 Document 放入一个共享数据库事务，也不得在 catch 中无条件删除 Blob。

---

## Task 7：Vue/OpenAPI/共享 TypeScript 契约覆盖门禁

**Files**

- Create: `contracts/openapi/vue-client-coverage-v1.json`
- Create: `scripts/openapi/validate-vue-client-contract-coverage.mjs`
- Create: `tests/openapi/vue-client-contract-coverage.test.mjs`
- Modify missing API modules under: `ui/admin/src/api/*.ts`
- Modify missing exports under: `packages/client-contracts/src/**`
- Add missing `contracts/openapi/*-v1.json` and corresponding contract tests only where a production Vue call truly lacks a fixture
- Modify: `package.json` only if `test:openapi` glob does not discover the new test
- Create after GREEN: `docs/verification/vue-openapi-client-contract-coverage-2026-08-08.md`

**Manifest entry**

```json
{
  "apiModule": "ui/admin/src/api/users.ts",
  "routePrefixes": ["/api/v1/identity/host/users"],
  "openApiFixture": "contracts/openapi/identity-host-users-v1.json",
  "clientContractModules": ["packages/client-contracts/src/identity-host-users.ts"]
}
```

**Required behavior**

- 枚举 `ui/admin/src/api/*.ts` 的生产文件，排除 `*.test.ts` 和唯一传输基础 `http.ts`；每个文件必须恰好有一条 manifest 记录。
- manifest 路径必须存在、使用仓库相对路径、无通配符；route prefix 必须同时出现在 API 模块和 OpenAPI fixture 中。
- 生产 API 模块必须从 `@fullnet/client-contracts` 导入公开 DTO/guard，不允许重新声明与后端响应同形的本地接口。
- client-contract 模块必须从根 `packages/client-contracts/src/index.ts` 导出；fixture 的核心字段变化由现有 breaking gate 锁定。
- 新增 API 模块缺少 fixture 或共享契约时直接失败；不允许用 `manual`, `legacy`, `TODO` 或目录级豁免绕过。

本 Task 不生成全量 SDK，不修改 Layui，也不把 UI 组件类型误当后端契约。

---

## Task 8：首个真实事件 v1→v2 演练（当前停止）

当前**不要创建 snapshot、RED、通用 upgrader 或空业务事件**。当某个已发布 Integration Event 出现真实非加法变更时，先新增单独 Spec，至少冻结：旧/新契约、相邻 upgrader、producer/consumer 发布顺序、最长 Outbox 保留与回滚窗口、双写或并行 Handler 策略、死信重放和旧版本退役扫描。只有该 Spec 获批后才生成 Cursor 代码任务。

字段尾部加法且旧消费者可安全忽略时保持同一 SchemaVersion 的兼容规则；不能为了演练人为制造版本升级。

## 最终 program 门禁

Tasks 1–7 全部完成后才执行 program merge snapshot；此前每个 Task 只更新自己的验证记录。最终必须证明：

- 权威 Markdown 不含无效 UTF-8 或 `U+FFFD`；
- npm/NuGet Critical/High 策略都失败关闭；
- CachePolicy Architecture allowlist 为 0；
- `module-local-transaction-debt.json` 为空，且没有删除扫描器或放宽匹配；
- 表访问和跨模块外键 debt 仍为空；
- 新投影和 claim 状态机通过 SQL Server/MySQL、恢复、乱序、重放、commit-unknown 与对账验证；
- Vue 生产 API 文件 100% 被精确契约 manifest 覆盖；
- Release solution build、Unit、Architecture、governance、OpenAPI、naming、SQL safety 和 fresh affected merge 全部使用新鲜输出；
- `git diff --check`、shared runner、Docker running/residual 均为 0。
