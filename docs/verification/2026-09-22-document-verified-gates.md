# Document Verified 门禁记录（2026-09-22）

## 范围

Host Document 一期/二期功能已在 `891981b9` 合入；本记录跟踪 **Verified 升档** 所需门禁，不重复功能开发。

## 本机 fresh 结果

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| admin-parity WCAG | `pnpm test:e2e:admin -- --grep "Document 管理页"` | 通过（axe 0 violations） |
| 治理 | `pnpm test:governance` | 通过 |
| OpenAPI / 客户端契约 | `pnpm test:openapi` | 通过 |
| 离线 OpenAPI 快照 | `node scripts/openapi/apply-document-openapi-delta.mjs` + `pnpm openapi:client:generate` + `pnpm openapi:client:snapshot -- --offline --check` | 通过 |

## 未在本环境关闭

| 项 | 说明 |
| --- | --- |
| admin-real-stack 双库 E2E | `pnpm test:e2e:real` 依赖 Testcontainers；本机 `Could not find a working container runtime strategy` |
| 运行时 OpenAPI snapshot | `pnpm openapi:client:snapshot -- --update` 依赖 `OpenApiDocumentationApiSqlServerTests` + Docker |
| DbUp 231/232 目标库执行 | 需 Migrator 或真实栈 bootstrap 对 SQL Server/MySQL 各执行一次并验证 retention/`IsHot` |

## 结论

**保持 `Build-verified`**，待 CI 或具备容器运行时的环境完成双库 real-stack 与运行时 snapshot 后，再将 `capability-status.md` / `adminnet-feature-parity.md` 中 Document 行升为 **Verified**。
