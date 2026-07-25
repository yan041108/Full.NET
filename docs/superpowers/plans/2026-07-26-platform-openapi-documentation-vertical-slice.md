# OpenAPI 与 Scalar 接口文档纵向切片（2026-07-26）

## 目标

将 Host API 的 OpenAPI 文档元数据、JWT Bearer 安全方案与 Scalar UI 固化为可测试契约，并在双管理端工作台提供入口。

## 清单

1. [x] `AddFullNetOpenApi` / `MapFullNetOpenApi`（标题、版本、Bearer 安全方案）
2. [x] Scalar UI 标题与 OpenAPI 路由模式对齐
3. [x] Integration **164 → 166**（SQL Server/MySQL）
4. [x] `platform-api-documentation-v1.json` + client-contracts
5. [x] Vue `OverviewView` + Layui 工作台 API 文档链接
6. [x] 路线图与验证记录

## 范围外

- 生产环境按环境变量关闭文档端点
- OpenAPI 破坏性变更 CI 门禁
- 多文档版本并存
