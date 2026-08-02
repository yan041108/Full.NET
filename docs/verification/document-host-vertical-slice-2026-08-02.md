# Document Host 文档库验证记录

- 日期：2026-08-02
- 状态：**Verified**
- 计划：[`2026-08-02-document-host-vertical-slice.md`](../superpowers/plans/2026-08-02-document-host-vertical-slice.md)

## 交付范围

Host 文档库纵向切片（`Full.NET.Modules.Document`）：

| 层级 | 内容 |
|---|---|
| 迁移 | `053_DocumentHostFoundation.sql`（SQL Server + MySQL）：分类、标签、文档项、版本、标签关联 |
| 文档 API | 分页列表/详情、创建/更新、版本绑定 `fileId`、软删/恢复 |
| 分类/标签 API | 列表/详情、创建/更新、软删；同级/全局名称唯一 |
| 权限与导航 | `document.host_documents.*`、`document.categories.manage`、`document.tags.manage`；Vue 三路由 |
| Vue UI | `HostDocumentItemsView`、`DocumentCategoriesView`、`DocumentTagsView` |
| 共享契约 | `host-document-items/categories/tags.ts` + OpenAPI `document-host-categories-tags-v1.json` |
| E2E | `host-documents.spec.mjs`（仅 Vue；Layui 跳过） |

**明确未交付**：租户文档库、ACL 细粒度 UI、外链分享、预览转换、病毒扫描。

## 验证矩阵（2026-08-02 新鲜输出）

| 门禁 | 结果 |
|---|---|
| `Migration053DocumentHostFoundationRecoveryTests` | **2/2**（SQL Server + MySQL） |
| `DocumentApiSqlServerTests` / `DocumentApiMySqlTests` | **6/6**（items、categories/tags、authorization） |
| `DocumentAuthorizationContributorTests` | **1/1** |
| `pnpm test:naming` | **24/24** |
| `pnpm --filter @fullnet/client-contracts test`（document 相关） | **4/4** |
| `pnpm --filter @fullnet/admin test`（document + navigation） | **14/14** |
| OpenAPI 夹具 `document-host-categories-tags-contract.test.mjs` | **2/2** |

**本机执行（2026-08-02）**：`npx playwright test host-documents --project=vue-admin` → **2/2**（SQL Server）；`FULLNET_E2E_DATABASE_PROVIDER=MySql` 同命令 → **2/2**（MySQL）。

## 提交序列（main）

1. `ac7605a` — 迁移 053 + 恢复测试
2. `deb6b06` / `01afb9c` — Files 契约抽取 + Host 文档项 API
3. `b01de86` — 分类/标签 API + OpenAPI
4. `91966c2` — 导航与 403 授权夹具
5. `08c3acb6` — Vue 三页 + client-contracts
6. `781da7fa` — 真实栈 E2E spec
