# OpenAPI 驱动客户端生成：Vue 生产 API 全量迁移收官验证（2026-08-23）

- 决策：`Migration-complete`
- 能力状态：`Build-verified`（Vue 生产 API 模块已全部迁入生成客户端；不等于公开 npm SDK 或 `Production-verified`）
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

`vue-client-coverage-v1.json` 所列 **45 个 Vue 生产 API 模块**均已登记 `client-generation-manifest-v1.json`，共 **230** 条 `generated` Operation。Document 模块最后一项 `document-statistics.ts` 已于 [`openapi-client-document-host-statistics-2026-08-22.md`](./openapi-client-document-host-statistics-2026-08-22.md) 通过切片门禁。

单模块迁移阶段正式收官。CRUD 代码生成器产出的 `clients/vue/*.generated.ts` 仍作为生成工作区产物存在，但 Catalog Product Golden File 已收敛到 OpenAPI 生成 Operation；手写与生成类型并存的模块在薄适配层通过显式 body 构造或 `unknown` 桥接保持类型安全。

## 范围摘要

| 指标 | 结果 |
| --- | ---: |
| Vue 生产 API 模块 | 45 |
| Manifest `generated` 条目 | 230 |
| `publicOperationIds` | 4（含 `documentPublicAccessDocumentShare`） |
| 最后切片 | `document-host-statistics`（1 op） |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 116/117（`openapi-breaking-change-gate` 相对 **未提交** HEAD 检测到 CodeGeneration 夹具追加 401/403，属契约元数据补齐，合入后即为新基线） |
| `pnpm --filter @fullnet/client-contracts test` | 138/138，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin test` | 143 文件 / 492 项，通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0（vue-tsc + Vite） |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `pnpm audit:clients` | 退出码 0 |
| `dotnet test … -c Release --filter FullyQualifiedName~CodeGeneration` | 49/49，通过（Templates/Catalog/Runs OpenAPI 401/403 与 `application/octet-stream` 下载契约已对齐） |
| `pnpm test:integration:affected -- --base dde01b32 --phase merge` | **143/143，通过**（2026-08-23 r3） |

## 合入前修补（2026-08-23 续）

CodeGeneration 模块 integration 切片曾缺 `ProducesProblem(403)` 与夹具状态码登记，导致 OpenAPI 契约断言失败。已补齐：

- `ManageHostTemplates`：Create / Update / Delete 追加 403
- `ManageHostRuns`：Preview 追加 403（Apply 等此前已具备）
- `BrowseHostCatalog`：column-sync 已具备 401/403
- 夹具：`code-generation-templates-v1.json`、`code-generation-catalog-v1.json`、`code-generation-runs-v1.json` 同步 401/403
- `CodeGenerationRunAssertions`：下载产物期望 `application/octet-stream`
- `OpenApiPilotContractAssertions`：新增 `isPublic` 参数，公开 Operation（`documentPublicAccessDocumentShare`）断言空 `security: []`
- Jobs 集成夹具：`HandlerKind` 显式写入缺失处理器用例；缩短 bounded-concurrency 失败键；HTTP secretHeaders 字典键大小写不敏感断言

## 未验证项

- 生产等价容量、真实栈 E2E 全矩阵与公开 npm SDK 发布不在本阶段范围。

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
