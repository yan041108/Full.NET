# P2 架构改造执行计划（后端解耦 + 前端 headless）

- 日期：2026-07-19
- 状态：提案 / 阶段 1 已提交（`cdf82c1`）；P2c headless 前端契约层已落地（2026-07-19）
- 来源：架构分析建议 P2；用户要求执行 P2 改造
- 前置：P1 已完成（提交 `109989e`，模块中间件/后台契约化，宿主不再直接引用模块）
- 范围：P2a 业务模块与 ASP.NET Core 解耦；P2c Vue/Layui 重复逻辑收敛到 headless 契约层。P2b（基础设施/业务比例）为流程建议，非代码改造，不在本计划内

## 1. 实测耦合基线

### Tenancy（32 个 .cs，仅 5 个与 Web 耦合）

- Web 耦合文件：`TenancyModule.cs`、`TenantResolutionMiddleware.cs`、`Features/*/Endpoint.cs`（3 个）
- Web 无关（Core 候选）：`Domain/`、`Features/*`（Command/Query/Handler/Validator/Service）、`Persistence/`、`Contracts/`、`Serialization/`、`Seeding/`、`TenancyAuthorizationContributor.cs`、`TenancyOptions.cs`、`TenantProvisionedCacheInvalidationHandler.cs`
- 结论：**可干净拆分**，核心占比高。

### Identity（约 17 个文件与 Web/认证耦合）

- Web/认证耦合：`IdentityModule.cs`、全部 `Endpoint.cs`（7 个）、`Authorization/`（PermissionHandler、PolicyProvider、Requirement、ResultHandler、EndpointExtensions）、`Http/IdentityCookieWriter.cs`、`Security/FullNetJwtBearerEvents.cs`、`Features/ManageSuperAdministrators/SuperAdministratorManagementService.cs`
- Web 无关（Core 候选）：`Domain/`、`Persistence/`、部分 `Features/*` 的 Command/Query/Handler、`Contracts/`、`Security/`（TokenHash、随机令牌、签名密钥环、JWT 签发的纯算法部分）、`Seeding/`、`Serialization/`
- 结论：**认证本质是 Web 关注点**，Core 只能容纳领域/持久化/部分处理器/契约；拆分收益低于 Tenancy，复杂度更高。

## 2. 目标边界

每模块拆为两个项目：

| 项目 | 职责 | 依赖约束 |
| --- | --- | --- |
| `Full.NET.Modules.{Module}`（Core） | Domain、Features 业务处理器、Persistence、Contracts、Serialization、Seeding、Options、领域授权贡献 | 禁止 `FrameworkReference Microsoft.AspNetCore.App`；只依赖 Abstractions/Data.Abstractions/Modularity 非 Web 面、FluentValidation、Extensions.* |
| `Full.NET.Modules.{Module}.Http` | `IFullNetModule` 实现、Endpoint、中间件、认证/授权/CORS/限流接线 | 依赖对应 Core；承载全部 ASP.NET Core 面 |

- Composition 改为引用各模块 `.Http` 项目；`.Http` 传递引用 Core。
- Contracts 仍属 Core（跨模块消费者只依赖 Core，不拖 Web）。

## 3. 分阶段执行（每阶段独立提交，测试先行）

- **阶段 0（RED 门禁）**：新增架构测试 `BusinessModuleCores_DoNotDependOnAspNetCore`，断言 Core 程序集不引用 `Microsoft.AspNetCore.*`。先失败。
- **阶段 1（Tenancy）**：新建 `Full.NET.Modules.Tenancy.Http`，迁移 5 个 Web 文件；Core 去除 AspNetCore FrameworkReference；更新 Composition、`DependencyRulesTests`（`typeof(TenancyModule).Assembly` 指向 Http，Contracts 断言指向 Core）、IntegrationTests 引用；Release 构建 + 四套测试。
- **阶段 2（Identity）**：同法拆分；重点处理认证/授权留在 Http，Core 仅保留可脱离 Web 的领域与持久化；更新全部依赖与测试。
- **阶段 3（收尾）**：架构测试转绿并覆盖两模块；更新 `delivery-map.md`、能力矩阵与命名规范（新增 `.Http` 子层惯例）。

## 4. 风险与取舍（须知晓后再执行）

- **与 YAGNI 规则张力**：当前仅 2 个模块、且 P1 已解决 Worker 拖入 HTTP 依赖的实际痛点，无"需要无 Web 内核"的真实消费者。仓库规则明确"禁止为目录完整而创建空层""没有真实消费者的抽象不要提前创建"。本改造收益主要是边界更纯，代价是项目数翻倍与测试面扩大。
- **Identity 收益有限**：认证天然属 Web，Core 偏薄。
- **不可逆性**：改变程序集/项目结构属兼容相关决策，回退成本高。

## 5. P2c 前端 headless（独立后续）

- 目标：将 Vue（`ui/admin`）与 Layui（`ui/admin-layui`）重复的会话、HTTP 客户端、导航白名单收敛到 `packages/client-contracts` 的 headless 层，双端只做渲染适配。
- 约束：保持 R-20260717-credentialed-cors 与 R-20260717-client-navigation-boundary；双端同场景 E2E 全绿才可标 `Verified`。
- 规模：大型前端重构，需独立规格与 `pnpm test:e2e` 验证，单独承接，不与后端阶段混提交。

## 6. 建议

后端 P2a 收益有限且与 YAGNI 规则冲突；若确定执行，建议按阶段 0→1→2→3 逐步提交，先做 Tenancy 验证模式再评估 Identity。前端 P2c 作为独立里程碑。请确认是否按此计划推进阶段 1。
