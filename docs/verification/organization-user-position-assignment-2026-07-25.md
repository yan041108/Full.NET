# Organization 用户-职位隶属验证记录（2026-07-25）

- 范围：`fn_organization_user_position`；租户用户-职位隶属 CRUD；Vue/Layui 管理页
- 计划：[实施计划](../superpowers/plans/2026-07-25-organization-user-position-assignment-vertical-slice.md)
- 增补日期：2026-07-29
- 状态：**Build-verified**（双管理端、双数据库真实写路径已通过；完整 `main` CI 与发布前人工验收待执行）

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | Organization user-positions SQL Server/MySQL 既有生命周期验证通过 |
| OpenAPI 夹具 | `organization-tenant-user-positions-v1` 静态契约通过 |
| client-contracts | `tenant-user-positions` 契约通过 |
| Vue 客户端 | API 单测与类型检查通过；使用用户职位专用候选 API，支持按需加载后续页与 403 空候选降级 |
| Layui 客户端 | 控制器测试通过；使用用户职位专用候选 API，支持按需加载后续页且不依赖 `/api/v1/identity/users` 或 `/api/v1/me` |
| Mock parity | 用户职位列表、分配与取消双端场景通过 |
| Real-stack SQL Server | Vue/Layui 经 UI 完成分配、设为主职位、取消，并由 API 回读持久化结果 |
| Real-stack MySQL | Vue/Layui 经 UI 完成分配、设为主职位、取消，并由 API 回读持久化结果 |

## 非目标

- 不包含候选用户关键字搜索；当前正式候选目录按稳定用户名分页，由客户端显式按需加载后续页。
- 不包含完整 `main` CI 与发布前人工验收。
