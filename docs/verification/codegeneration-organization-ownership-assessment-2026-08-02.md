# CodeGeneration 组织归属（OrganizationUnit）生成评估建议稿

- 日期：2026-08-02
- 代码基线：`main` @ `16d8a26`
- 状态：**建议稿**（未经 Spec 批准，不得进入实施计划或生产代码）
- 上游证据：[Admin.NET 生命周期吸收验证](codegeneration-adminnet-lifecycle-2026-07-30.md)、[生命周期 SQL 运行时矩阵](codegeneration-lifecycle-runtime-sql-2026-08-02.md)

## 1. 结论

`FullNetCrudOwnershipMode.OrganizationUnit` 已在 Schema/CLI 建模，但 `CrudArtifactGenerator` 对可执行产物 **fail-closed**：缺少可信组织写入授权端口时抛出 `NotSupportedException`。`OrganizationUnitId` 不得进入客户端可写契约，也不能由请求字段充当授权事实。

建议下一纵向切片在 **Organization 模块提供稳定授权端口** 后，交付组织归属实体的生成 SQL/Feature/Endpoint，并复用生命周期运行时矩阵模式做双库验证。

## 2. 建议纳入

1. **端口**：例如 `IOrganizationUnitWriteAuthorization`（命名待定），由 Organization 模块实现；生成 Feature 在 Create/Update/Delete 前校验 actor 对目标 `OrganizationUnitId` 的写入权。
2. **生成**：`OrganizationUnitId` 列由服务端赋值或校验后绑定；列表/分页 SQL 注入组织过滤谓词（与 Identity 运行时数据范围对齐）。
3. **测试**：Unit（生成产物片段）+ Integration 双库运行时矩阵（参照 `LifecycleRuntimeSqlTestSupport`）。
4. **双端**：Vue/Layui 页面模型只读展示组织列，写入走服务端授权。

## 3. 明确排除

- 客户端提交 `OrganizationUnitId` 作为授权依据
- Tree/MasterDetail 可执行生成
- 无 Organization 模块依赖的跨模块猜测

## 4. 未决问题（Spec 前）

1. 授权端口放在 Organization 还是 Abstractions？是否复用现有用户-机构隶属 API？
2. Host/Global 作用域与组织列是否互斥？
3. 与角色数据范围（`identity.runtime-data-scope`）的优先级与组合规则。

## 5. 规则/Skill

未触发规则或 Skill 升级触发条件；实施时沿用 `fullnet-module-delivery` 与双库门禁。