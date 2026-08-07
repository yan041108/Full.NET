# Vue/OpenAPI/共享 TypeScript 契约覆盖验证（2026-08-08）

## 范围

Task 7：为 `ui/admin` 全部 38 个生产 API 模块建立精确 manifest，并强制：

- 每个模块恰好一条 `contracts/openapi/vue-client-coverage-v1.json` 记录
- route prefix 同时出现在 API 源码与 OpenAPI 夹具
- DTO/guard 来自 `@fullnet/client-contracts`，禁止本地后端同形接口
- client-contract 模块从 `packages/client-contracts/src/index.ts` 导出

## 交付物

| 产物 | 说明 |
|------|------|
| `contracts/openapi/vue-client-coverage-v1.json` | 38 条生产 API manifest |
| `scripts/openapi/validate-vue-client-contract-coverage.mjs` | 覆盖门禁脚本 |
| `tests/openapi/vue-client-contract-coverage.test.mjs` | 自动化门禁测试 |
| 6 个新增 OpenAPI 夹具 + 契约测试 | diagnostic-policy、document items、host-user-management、serial-numbers、super-administrators、totp |
| `packages/client-contracts` | `settings-diagnostic-policy.ts`、`host-user-organization-reference.ts`、`CreateHostApiKeyRequest` |
| `identity-host-roles-v1.json` | 合并 authorization-tree 与 data-scope 路径供 `roles.ts` 单夹具映射 |

## 验证命令（新鲜输出）

```text
pnpm test:openapi
# 81 tests, 0 fail
node scripts/openapi/validate-vue-client-contract-coverage.mjs
# Vue client contract coverage validation passed.
```

## 结果

- 生产 API 模块：38/38 manifest 覆盖
- 本地后端 DTO 残留：`diagnostic-policy.ts`、`api-keys.ts`、`host-user-organization-reference.ts` 已迁移至 client-contracts
- OpenAPI 测试：81/81 通过