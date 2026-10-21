# Enterprise Request 样例

主从（header + line）租户单据样板，配合 Full.NET CRUD 生成器演示 F09 业务接入标准。

## 内容

- `schema.json` — `master.detail` CRUD Schema（申请头 `enterprise_request` + 明细 `enterprise_request_line`）
- `integration-target.json` — 模块/Composition/Vue 路由接入目标
- `src/` — 最小模块骨架（`EnterpriseRequestModule.cs`）
- `tests/schema.test.mjs` — Schema 结构契约测试

## 装配边界

官方 Full / Enterprise 预设与三个官方宿主均不包含此示例，也不创建 `demo_*` 表。示例是应用自有模块，应用组合根显式引用示例项目后使用：

```csharp
services.AddFullNetApplicationModules(configuration, FullNetHostProfile.Api,
    [new EnterpriseRequestModule()]);
```

Worker 使用相同静态模块清单和 `FullNetHostProfile.Worker`。应用 Migrator 单独加载示例程序集 `Migrations.SqlServer` 或 `Migrations.MySql` 下的嵌入脚本；官方迁移程序集不包含示例 SQL。应用模块目录来源为 Application。集成测试通过 `includeEnterpriseRequest: true` 显式装配模块及示例迁移。

`integration-target.json` 是开发者主动执行生成器时的接入目标，执行该接入会修改目标组合根；它不表示官方预设默认启用示例。

## 快速开始

```bash
# 预览生成
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema samples/enterprise-request/schema.json --workspace .

# 规划接入
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema samples/enterprise-request/schema.json \
  --repository . \
  --target samples/enterprise-request/integration-target.json
```

## E2E

管理端路由 `/enterprise-requests` 由 `tests/e2e/admin-real-stack/tests/enterprise-request.spec.mjs` 桩测试覆盖。
