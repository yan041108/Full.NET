# 从模板到首个 CRUD（实走摘要）

1. 组装并创建应用（见 [`templates/fullnet-app/README.md`](../../templates/fullnet-app/README.md)）。
2. 诊断：`pnpm run diagnose:development`（应用根目录）。
3. 配置 `ConnectionStrings:app` 与 Migrator：`dotnet run --project framework/fullnet/src/Hosts/Full.NET.Host.Migrator -- --seed development`。
4. 使用 [`Full.NET.CodeGeneration.Cli`](../../src/Tools/Full.NET.CodeGeneration.Cli/) 在应用工作区生成租户 CRUD（表名遵循 `{owner}_*` 命名，见 [`samples/enterprise-request`](../../samples/enterprise-request)）。
5. 二次生成前在 Handler/SQL 中保留人工修改；生成器只应更新其登记产物（见 CodeGeneration 单元测试 `ModuleIntegrationPlannerTests`）。

验证：`dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~CodeGeneration"` 与 `pnpm test:templates`。
