# Tenancy Host 租户管理纵向切片验证记录

- 日期：2026-07-23
- 类型：纵向切片交付验证
- 状态：**Build-verified**（双库 Integration + Mock/SQL Server 真实栈 E2E 已通过；MySQL 真实栈待 CI）
- 计划：[`2026-07-21-tenancy-admin-vertical-slice.md`](../superpowers/plans/2026-07-21-tenancy-admin-vertical-slice.md)

## 范围

Host 作用域租户：分页列表、详情、开通（事务 Outbox）、更新名称、禁用；`tenancy.host_tenants.read` / `tenancy.tenants.write`；Vue/Layui 对等管理页；双库 Integration；OpenAPI 夹具；Mock/真实栈 E2E。

**明确未交付**：租户套餐/订阅、独立数据库租户、Identifier/Domain 变更、Production 维护窗口命名升级实跑。

## 后端

| 端点 | 权限 | 状态码 |
| --- | --- | --- |
| `GET /api/v1/tenancy/tenants` | `tenancy.host_tenants.read` | 200 |
| `GET /api/v1/tenancy/tenants/{id}` | `tenancy.host_tenants.read` | 200 |
| `POST /api/v1/tenancy/tenants` | write | 201 |
| `PUT /api/v1/tenancy/tenants/{id}` | write | 200 |
| `POST /api/v1/tenancy/tenants/{id}/disable` | write | 200 |

稳定错误码：`tenancy.tenant.version_conflict`、`tenancy.tenant.last_remaining`。

Host 目录读权限为 `tenancy.host_tenants.read`（与租户上下文 `tenancy.tenants.read` 分离）；Migrator `AddMigrationServices` 已注册 `ICurrentTenant`，修复真实栈 Seed 启动失败。

集成测试：`Host_tenant_management_returns_standard_contract`（SQL Server + MySQL；含 OpenAPI 运行时断言）。

## 客户端

- 共享契约：`packages/client-contracts/src/host-tenants.ts`
- Vue：`TenantsView.vue`、`api/tenants.ts`、`navigation/catalog.ts`（`tenants` 白名单）
- Layui：`js/core/tenants.js`
- Mock parity E2E：`shell-parity.spec.mjs`「租户列表、开通与禁用在两端保持一致」
- OpenAPI 静态夹具：`contracts/openapi/tenancy-host-tenants-v1.json`（`pnpm test:openapi` **16/16**）

## 本地验证（2026-07-23）

| 命令 | 结果 |
| --- | --- |
| `dotnet build -c Release` | 0 警告 / 0 错误 |
| Unit `--minimum-expected-tests 349` | **349/349** |
| Architecture `--minimum-expected-tests 36` | **36/36** |
| Integration `Host_tenant_management` | **2/2**（~2m 20s） |
| `pnpm test:openapi` | **16/16** |
| `pnpm test:clients` | 通过（管理端 **165**） |
| Parity E2E 全量 | **42/42** |
| 真实栈 E2E 全量（SQL Server） | **42/42**（~2m 12s） |
| 真实栈 E2E 全量（MySQL） | **41/42**（Layui `session-cross-tab` 超时；已修复，见下） |
| `session-cross-tab` 修复验证（SQL Server） | **2/2**（Vue + Layui） |

## 真实栈验收要点

`host-tenants.spec.mjs` 与全量真实栈均已覆盖：

1. Host 管理员进入租户管理，列表含种子 `Full.NET Local` / `local · localhost`。
2. `e2e-viewer`（仅有 `tenancy.tenants.read`，无 `tenancy.host_tenants.read`）调用租户目录 API 返回 `authorization.permission_denied`，导航无租户管理，直链 `#/tenants` 展示 403。

## 结论

Host 租户管理 API 与双端 UI 已对齐 Identity/Organization 切片模式；`adminnet-feature-parity`「租户管理」保持 **Build-verified**。租户套餐、完整对标验收与真实栈全绿后，方可升格 `Verified`。
