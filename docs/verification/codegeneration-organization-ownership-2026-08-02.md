# CodeGeneration 组织归属（OrganizationUnit）生成验证

- 日期：2026-08-02
- 代码基线：`main` @ `720223a`
- 状态：**Build-verified**（双库 Integration 需 Docker/Testcontainers）
- 设计：[`2026-08-02-codegeneration-organization-ownership-design.md`](../superpowers/specs/2026-08-02-codegeneration-organization-ownership-design.md)
- 计划：[`2026-08-02-codegeneration-organization-ownership.md`](../superpowers/plans/2026-08-02-codegeneration-organization-ownership.md)

## 交付摘要

| 切片 | 提交 | 说明 |
|------|------|------|
| 写入授权端口 | `d98bf76` | `IOrganizationOwnedEntityWriteAuthorizer` + Unit |
| 生成器解除 fail-closed | `720223a` | Feature/SQL/Endpoint 片段 + 双库运行时矩阵 |

## 验证矩阵

| 类别 | 测试 | 结果 |
|------|------|------|
| Organization 端口 | `OrganizationOwnedEntityWriteAuthorizerTests` (3) | Unit GREEN |
| 生成器产物 | `CrudArtifactGeneratorTests.Generate_organization_owned_*` | Unit GREEN |
| 生成器回归 | `CrudArtifactGeneratorTests` (26) | Unit GREEN |
| 双库 SQL 运行时 | `GeneratedLifecycleSqlRuntimeIntegrationTests` 组织归属 SoftDelete (2) | 需 Docker；本地编译通过 |

## 行为要点

- `FullNetCrudOwnershipMode.OrganizationUnit` 可生成可执行产物；Tree/关系场景仍 fail-closed。
- `OrganizationUnitId` 不出现在 Create/Update 客户端可写契约；Create 通过 `X-FullNet-Organization-Unit-Id` 受信头绑定。
- Feature 在 Create/Update/Delete 前调用 `IOrganizationOwnedEntityWriteAuthorizer`；列表/详情 SQL 追加 `IDataScopeSqlFilterBuilder.BuildOrganizationUnitFilter`。

## 未交付

- Vue/Layui 双端 E2E 与 `Verified` 标记
- Host/Global 作用域与组织列组合
- Tree/MasterDetail/ManyToMany 组织归属生成

## 规则/Skill 复盘

未触发规则或 Skill 升级条件；沿用 `fullnet-module-delivery` 与双库门禁。