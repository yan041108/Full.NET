# Wave 2 — B2 Verified 门禁进度（2026-09-22）

**规则**：无 fresh 双库 real-stack + WCAG 证据不得将 parity 行改为 `Verified`。本记录仅登记本机已执行项。

## 本机已执行（fresh）

| 项 | 命令/结果 | 说明 |
|----|-----------|------|
| 横切治理 | `pnpm test:governance` | 55/55 通过 |
| OpenAPI/客户端 | `pnpm test:openapi` | 175/175 通过（manifest 557 与 `identityRetireHostUser` 对齐） |
| Tenancy F08a | `dotnet test` …`TenantQuota_metric_id_reconciliation` | SqlServer + MySql 双库通过 |
| DataApproval 01 | `dotnet test` …`DataApprovalApiSqlServerTests` | 场景目录双库契约通过 |
| HTTP JSON 源生成 | `SerializationRulesTests.ProductionSerialization` | `ProvisionTenantMemberRequest` + OIDC 客户端/授权 DTO；门禁配置 `Identity:Oidc:Enable=true`（2026-09-22 本机） |

## CI 观测

| 推送 | Workflow / Run | 结论（滚动更新） |
|------|----------------|------------------|
| `b11806fb` | `ci` / `35712818129` | 失败：`build-test`（dotnet 发现数门禁）、`real-stack-e2e-mysql` 等（文档/代码生成等超时或 500，非 B2 新增 spec 专项结论） |
| `038ec463` | `api-native-aot-linux` / `35728061700` | 失败：OIDC E2E（~30s，非启动超时）；`Connection refused localhost:5001`（Native `HttpClient` 自动跟跳 redirect_uri；已修 `AllowAutoRedirect=false`） |
| `03ec7706` | `api-native-aot-linux` / `35735833544` | 失败：OIDC 在线会话撤销等仍 500（`IdentityOidcApplication` 缺 AOT materializer；已改为 `IdentityOidcApplicationRow` 待推送） |
| `03ec7706` | `ci` / `35735833767` | 失败：`real-stack-e2e*`（`loginAsHostAdmin` 等导航超时、`identity/roles` 403）、`integration-shard*`、`build-test`/`client-build-test`；`worker-native-aot-linux` 绿 |
| `780b37bb` | `api-native-aot-linux` / `35750315918` | 推送后观测中（OIDC revoke 行类型 + real-stack 登录等待 navigation） |
| `60924229` | RBAC closeout | `vue-action-authorization-w4-w5-closeout` 登记 integration-shard 证据口径 |
| 本机 | Ai AOT materializer | `AiAgentApprovalRecord` 登记于 `AiBudgetRowReaders`（OIDC 消费者 E2E 500 修复）；`pnpm test:aot:analyzers` 绿 |
| `a9eff2df` | `api-native-aot-linux` / `35708760161` | 失败：OIDC E2E 启动超时；`NoMetadataForType` @ `MapPost77`（OIDC 管理 DTO 未登记，已于 `038ec463` 修复） |
| `a9eff2df` | `ci` / `35708760050` | 失败：多 job 红；`integration-matrix` 曾绿 |
| `f3ce3710` | `35698509441` | `api-native-aot-linux` 红（JSON 元数据，已修于 `9187daa5`） |

本机（`a9eff2df`）：Release build、`pnpm test:openapi` 175/175、`SerializationRulesTests` 绿；正在本地复跑 `pnpm test:integration:api:sqlserver`。

## 仍待环境（未升 Verified）

| ID | 模块 | 待执行 |
|----|------|--------|
| V1 | RBAC | program affected merge E2E 全绿 |
| V2 | SerialNumbers | `pnpm test:e2e:real` 流水号 spec fresh |
| V3 | Document | 双库 admin-real-stack + runtime `openapi:client:snapshot --update` |
| V4 | Tenancy/Org/Files | admin-parity Document WCAG 已绿；Tenancy/Org/Files 子集待扩路径（real-stack：`host-tenants`、`host-files` 等已有列表 spec） |
| V5 | Identity | 扩展 `host-users.spec.mjs` 退役用例在 CI real-stack 绿 |

## 新增 real-stack 规格（待 CI）

- `data-approval-scenarios.spec.mjs`
- `workflow-instances.spec.mjs`
- `host-users.spec.mjs` 退役场景
- `data-approval-requests.spec.mjs`（列表/详情）
- `workflow-todos-history.spec.mjs`（待办/已办页签）
- `workflow-forms.spec.mjs` 子表列配置发布冒烟
