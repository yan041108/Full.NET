# OpenAPI 与 Scalar 接口文档验证（2026-07-26）

## 摘要

固化 Host OpenAPI 文档元数据、Bearer JWT 安全方案与 Scalar UI 路由；双管理端工作台提供 API 文档入口。

| 维度 | 结果 |
| --- | --- |
| OpenAPI | `GET /openapi/v1.json`（标题、Bearer scheme、`/api/v1/**` 路径） |
| Scalar UI | `GET /scalar/v1`（HTML 200） |
| Integration 双库 | `OpenApi_documentation` SQL Server/MySQL **2/2** → **164 → 166** |
| 契约夹具 | `platform-api-documentation-v1.json` |
| client-contracts | `platform-api-documentation.ts` + Vitest **68/68** |
| 双端 UI | `OverviewView.vue` + Layui `overview-dashboard.js` |
| 四处 canonical 门槛 | **351/7/40/166** |

## 关联

- [实施计划](../superpowers/plans/2026-07-26-platform-openapi-documentation-vertical-slice.md)
- [Admin.NET 对标矩阵](../roadmap/adminnet-feature-parity.md)
