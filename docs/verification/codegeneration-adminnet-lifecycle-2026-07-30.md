# CodeGeneration Admin.NET 生命周期设计吸收验证

- 日期：2026-07-30
- 基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`adminnet-absorb-02-codegen-lifecycle`
- 状态：`Build-verified`
- 对应计划：[Admin.NET 设计吸收改造实施计划 Task 2](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md#task-2-生成软删除审计并发和场景安全-sql)

## 已落地

1. `FullNetCrudSchema` 显式建模 `Single`、`Tree`、`MasterDetail`、`ManyToMany` 场景及关系两端实体键、列名和数据作用域；
2. `Single` 场景按声明生成软删除、硬删除、创建/更新/删除审计和 `Version` 乐观并发 SQL，不再强制所有实体携带 `IsActive` 或完整审计字段；
3. 创建、更新、删除审计值由服务端 `IClock` 和认证主体生成，租户、审计、删除、版本及组织归属字段不进入客户端可写契约；
4. `Immutable` 采用追加型语义：保留创建和读取，禁止更新与删除端点；
5. CLI 和生成报告使用显式小写点分机器值；pre-1.0 PascalCase 输入通过逐项字面别名兼容，不依赖 CLR 枚举名；
6. legacy `hasVersion` 报告明确输出 `legacyLifecycle=disable`，不再把兼容占位能力误报为 `HardDelete`；
7. SQL Server/MySQL 迁移草案继续从已声明字段确定性生成，legacy SQL 与双库迁移 fixture 保持不变。

## 安全关闭

- `Tree` 只允许 Schema/CLI 建模；在同租户父节点校验、悬挂节点防护和环检测落地前，公共生成入口拒绝可执行产物；
- `MasterDetail`、`ManyToMany` 在聚合事务、复合键和级联语义明确前拒绝可执行产物；
- `OrganizationUnit` 所有权在可信组织写入授权端口存在前拒绝生成，客户端 `OrganizationUnitId` 不作为授权事实；
- 对于没有业务可写字段、更新审计或 `Version` 的可更新实体，Schema 校验阶段直接拒绝，避免出现“Schema 合法但生成失败”。

## 新鲜验证

```text
dotnet test Full.NET.UnitTests ... CodeGenerationCliTests|FullNetCrudSchemaTests|CrudArtifactGeneratorTests
PASS

dotnet test Full.NET.IntegrationTests ... ModuleIntegrationCompilationTests
PASS

pnpm test:sql-safety
PASS

pnpm test:naming
PASS

pnpm test:integration:affected --snapshot adminnet-absorb-02-codegen-lifecycle --phase slice
Release build: 0 warning / 0 error
CodeGeneration + Files + Realtime 双 Provider affected: PASS
Docker teardown: running=0 / residual=0
```

显式生命周期编译投影同时使用 Node 的 TypeScript 类型擦除语法检查和 ESM JavaScript 语法检查验证全部生成客户端文件。

## 证据边界

- 当前双库证据验证迁移模板形状、生成 SQL 安全规则、编译投影和受影响 Integration 集合；
- `SoftDelete`、`HardDelete` 与 `Immutable` 显式生命周期 SQL 已在双库隔离环境执行运行时矩阵（见 [codegeneration-lifecycle-runtime-sql-2026-08-02.md](codegeneration-lifecycle-runtime-sql-2026-08-02.md)）；更广 E2E 仍待补，因此不标记为 `Verified`；
- `Tree`、聚合关系和组织所有权仅完成安全建模与 fail-closed，不计为可执行功能；
- 本切片没有复制 Admin.NET.Pro 模板或运行时框架代码，只吸收其能力目录和场景划分思想。
