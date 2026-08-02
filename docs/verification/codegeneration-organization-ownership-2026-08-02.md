# CodeGeneration 组织归属（OrganizationUnit）生成验证

- 日期：2026-08-02
- 代码基线：`main` @ `1f03ccf`
- 状态：**Build-verified**（双库 Integration 需 Docker/Testcontainers）
- 设计：[`2026-08-02-codegeneration-organization-ownership-design.md`](../superpowers/specs/2026-08-02-codegeneration-organization-ownership-design.md)
- 计划：[`2026-08-02-codegeneration-organization-ownership.md`](../superpowers/plans/2026-08-02-codegeneration-organization-ownership.md)

## 交付摘要

| 切片 | 提交 | 说明 |
|------|------|------|
| 写入授权端口 | `d98bf76` | `IOrganizationOwnedEntityWriteAuthorizer` + Unit |
| 生成器解除 fail-closed | `720223a` | Feature/SQL/Endpoint 片段 + 双库运行时矩阵 |
| 模块编译集成 | `fca4554` | `validate-module-integration` + `Organization.Contracts` |
| Host/Global 互斥 | `246613a` | Schema 层拒绝 `organization.unit` 与非租户作用域组合 |
| CLI/预览互斥 | `c3a727d`–`e7db0ed` | JSON 加载、Preview Service 与 Integration API `invalid_schema` |
| 场景 fail-closed | `cfae6d8` | Tree/关系 + `organization.unit` 仍拒绝可执行生成 |
| E2E Schema 助手 | `848c20f` | `organization-owned-codegen-schema.test.mjs` provisioner |
| Runs 跟踪预览 | `494cd3f` | `CodeGenerationRunAssertions` 组织归属 `runs/preview` |

## 验证矩阵

| 类别 | 测试 | 结果 |
|------|------|------|
| Organization 端口 | `OrganizationOwnedEntityWriteAuthorizerTests` (3) | Unit GREEN |
| 生成器产物 | `CrudArtifactGeneratorTests.Generate_organization_owned_*` | Unit GREEN |
| 生成器 fail-closed | Tree/关系 + `organization.unit` 仍 `NotSupportedException` | Unit GREEN |
| 生成器回归 | `CrudArtifactGeneratorTests` (26) | Unit GREEN |
| Schema 互斥 | `FullNetCrudSchemaTests` + `CodeGenerationCliTests` host/global + org | Unit GREEN |
| 预览 API/UI | `Preview_organization_owned_*` + 双端 previews E2E | Unit GREEN；Preview/Runs API host/global 互斥 Integration；E2E 需真实栈 |
| Apply 工作区 | `host-code-generation-templates` 组织归属 Apply E2E | 需真实栈；落盘 Feature 含授权片段 |
| 模块编译集成 | `ModuleIntegrationCompilationTests.Organization_owned_explicit_backend_compiles_with_organization_references` | Integration GREEN |
| 双库 SQL 运行时 | `GeneratedLifecycleSqlRuntimeIntegrationTests` 组织归属 SoftDelete (2) | 需 Docker；本地编译通过 |

## 行为要点

- `FullNetCrudOwnershipMode.OrganizationUnit` 可生成可执行产物；Tree/关系场景仍 fail-closed。
- `OrganizationUnitId` 仅允许与 `TenantRequired` 组合；`HostOnly`/`Global` 在 Schema 层 fail-closed。
- `OrganizationUnitId` 不出现在 Create/Update 客户端可写契约；Create 通过 `X-FullNet-Organization-Unit-Id` 受信头绑定。
- Feature 在 Create/Update/Delete 前调用 `IOrganizationOwnedEntityWriteAuthorizer`；列表/详情 SQL 追加 `IDataScopeSqlFilterBuilder.BuildOrganizationUnitFilter`。

## 未交付

- `Verified` 标记仍待真实栈 E2E 与双库运行时矩阵全绿后关闭
- Tree/MasterDetail/ManyToMany 组织归属可执行生成（当前 fail-closed，非首切片范围）

## 规则/Skill 复盘

未触发规则或 Skill 升级条件；沿用 `fullnet-module-delivery` 与双库门禁。