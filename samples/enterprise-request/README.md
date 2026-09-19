# Enterprise Request 样例

主从（header + line）租户单据样板，配合 Full.NET CRUD 生成器演示 F09 业务接入标准。

## 内容

- `schema.json` — `master.detail` CRUD Schema（申请头 `enterprise_request` + 明细 `enterprise_request_line`）
- `integration-target.json` — 模块/Composition/Vue 路由接入目标
- `src/` — 最小模块骨架（`EnterpriseRequestModule.cs`）
- `tests/schema.test.mjs` — Schema 结构契约测试

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
