# 角色与数据授权对标收口（2026-07-26）

## 目标

将 Admin.NET 对标矩阵中「角色与数据授权」从 `Mapped` 更新为 `Build-verified`，汇总既有 Host 角色、权限、数据范围与用户-角色分配纵向切片证据。

## 清单

1. [x] Host 角色 CRUD 与权限替换（`identity.roles.*`）
2. [x] Host 角色数据范围读写（`015_HostRoleDataScope.sql`）
3. [x] 用户-角色分配 API 与双端 UI
4. [x] 运行时多角色数据范围并集 + 机构过滤（Organization 只读查询）
5. [x] Integration / OpenAPI / E2E 既有夹具（无新增门槛）
6. [x] 对标矩阵与验证记录

## 范围外

- 租户内角色管理（仅 Host 作用域已交付）
- 业务模块全面接入机构过滤
- `Verified` 标记（仍缺更广真实栈与人工验收）
