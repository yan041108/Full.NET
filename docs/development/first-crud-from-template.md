# 从模板到首个 CRUD（实走摘要）

1. 组装并创建应用（见 [`templates/fullnet-app/README.md`](../../templates/fullnet-app/README.md)）。
2. 诊断：`pnpm run diagnose:development`（应用根目录）。
3. 配置 `ConnectionStrings:app` 与 Migrator：`dotnet run --project framework/fullnet/src/Hosts/Full.NET.Host.Migrator -- --seed development`。
4. 使用 [`Full.NET.CodeGeneration.Cli`](../../src/Tools/Full.NET.CodeGeneration.Cli/) 在应用工作区生成租户 CRUD（表名遵循 `{owner}_*` 命名，见 [`samples/enterprise-request`](../../samples/enterprise-request)）。
5. 二次生成前在 Handler/SQL 中保留人工修改；生成器只应更新其登记产物（见 CodeGeneration 单元测试 `ModuleIntegrationPlannerTests`）。

验证：`dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~CodeGeneration"` 与 `pnpm test:templates`。

企业申请样例的创建入口通过 `X-FullNet-Organization-Unit-Id` 传递机构选择，服务端按当前租户与用户校验组织权限；JSON 只包含业务可写字段，创建/修改/删除操作人由服务端确定。浏览器验收入口为 `enterprise-request.spec.mjs --project vue-admin`，实际租户 CRUD 与 API 的审批、CSV 导入验收分别记录。

生成客户端读取器会按 OpenAPI 明确声明的整数 JSON 编码，将合法整数字符串归一为既有 TypeScript `number`，并拒绝超出安全整数范围的值；不会转换金额字符串或其他普通文本。嵌套引用、数组和联合分支使用相同边界。需要超过 `Number.MAX_SAFE_INTEGER` 的业务值时，应另行定义显式字符串契约，不能静默截断精度。
