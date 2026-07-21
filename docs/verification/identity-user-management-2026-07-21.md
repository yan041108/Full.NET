# Identity Host 用户管理纵向切片验证记录

- 日期：2026-07-21
- 类型：纵向切片交付验证
- 状态：Build-verified（非 `Verified`：缺生产 MFA、角色/菜单 CRUD、完整对标人工门禁）
- 计划：[`2026-07-21-identity-user-management-vertical-slice.md`](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)

## 范围

Host 作用域用户：分页列表、详情、创建、更新资料、禁用；`identity.users.read` / `identity.users.write`；Vue/Layui 对等管理页；双库 Integration；Mock parity E2E；真实栈最小冒烟。

**明确未交付**：角色/菜单/组织 CRUD、OpenAPI 夹具、Production 超级管理员 MFA。

## 后端

| 端点 | 权限 | 状态码 |
| --- | --- | --- |
| `GET /api/v1/identity/users` | `identity.users.read` | 200 |
| `GET /api/v1/identity/users/{id}` | `identity.users.read` | 200 |
| `POST /api/v1/identity/users` | `identity.users.write` | 201 |
| `POST /api/v1/identity/users/{id}/disable` | `identity.users.write` | 200 |
| `PUT /api/v1/identity/users/{id}` | `identity.users.write` | 200（乐观 `Version`；冲突 `identity.profile_version_conflict` 409） |

集成测试：`Host_user_management_*`（SQL Server + MySQL；含更新资料乐观版本冲突）。

## 客户端

- 共享契约：`packages/client-contracts/src/host-users.ts`
- Vue：`UsersView.vue`、`api/users.ts`
- Layui：`js/core/users.js`
- Mock parity E2E：`shell-parity.spec.mjs`「用户列表、创建与禁用在两端保持一致」
- 客户端单测门槛：管理端与共享包 **138** 项；parity E2E **32** 项（16 唯一场景 × 双端）

## 真实栈冒烟

新增 `tests/e2e/admin-real-stack/tests/host-users.spec.mjs`：

1. Host 管理员登录后进入用户管理，列表含种子 `admin` / `系统管理员`。
2. `e2e-viewer` 调用用户 API 返回 `authorization.permission_denied`，导航无用户管理，直链 `#/identity/users` 展示 403。

套件总项数：**20**（10 唯一场景 × Vue/Layui）；CI `real-stack-e2e`（SQL Server）与 `real-stack-e2e-mysql`（main）。

本地验证：2026-07-21 本机无可用容器运行时（Testcontainers 无法启动 SQL Server），未附新鲜 `pnpm test:e2e:real` 日志；以 CI 作业为准。

## OpenAPI 夹具

- 冻结清单：`contracts/openapi/identity-host-users-v1.json`
- 静态门禁：`pnpm test:openapi`（校验夹具结构与 C#/Endpoint 源码对齐）
- 运行时门禁：Integration `OpenApiHostUsersContractAssertions` 拉取 `/openapi/v1.json` 校验路径、状态码与 schema 属性

## 结论

首个业务纵向切片已证明模块交付流程可复制；`adminnet-feature-parity`「用户管理」保持 `Implementing`，待角色/菜单与完整对标验收后再升格。
