# Identity 用户管理纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。行为变更必须先失败测试再实现。未完成当前切片退出条件前，不得横向铺开角色/菜单/组织全量 CRUD。

- 建立日期：2026-07-21
- 状态：**已批准**（项目所有者 2026-07-21 确认：用户管理作为首个业务纵向切片；Vue/Layui 长期并行，本切片必须双端同步）
- 批准依据：
  - 项目所有者在外部分析吸收任务中的明确确认
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §“先做可运行的纵向切片”
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「用户管理」Core / M2
  - [`external-review-2026-07-21.md`](../../verification/external-review-2026-07-21.md) 吸收结论与决策附录
- 关联 Skill：`fullnet-module-delivery`
- 当前能力总览：[`capability-status.md`](../../roadmap/capability-status.md)

**Goal:** 交付首个可重复的后台业务纵向切片——Host 作用域用户列表/详情/创建/更新/禁用（不含完整角色 CRUD、菜单 CRUD、组织），证明模块交付流程、双库、双管理端与真实栈冒烟可复制。

**Architecture:** 继续模块化单体；Identity 模块内 CQRS + Dapper 显式 SQL；权限码进入授权目录；超级管理员动态投影仍适用；Vue/Layui 同场景同步；Admin.NET 包络仅走 Compatibility。

**Tech Stack:** .NET 10、Dapper、DbUp、SQL Server/MySQL、FluentValidation、ProblemDetails、Vue 3 + Element Plus、Layui 2、Playwright real-stack、Microsoft.Testing.Platform、Testcontainers。

---

## 范围与非目标

### 本切片必须交付

1. Host 管理员对用户的分页查询、详情、创建、更新基础资料、启用/禁用。
2. 稳定权限码（例如 `identity.users.read` / `identity.users.write`；最终以 Naming Profile 与授权目录为准）。
3. SQL Server/MySQL 迁移、唯一约束、租户/Host 作用域 SQL、审计字段。
4. Vue 与 Layui 对等管理页 + mock parity E2E + 至少一条真实栈冒烟（列表或禁用）。
5. 最后一名超级管理员保护路径不被本切片绕过；禁用/删除超管走既有领域服务边界。

### 明确非目标（下一切片）

- 角色 CRUD、菜单/按钮 CRUD、数据范围、组织/职位。
- 租户级终端用户自助注册或租户管理员登录模型变更。
- Art Design Pro 壳层迁移、富文本、文件上传。
- 完整“用户管理 = Admin.NET 对等”全部子功能。

---

## 退出条件

全部满足后可将 capability-status「最小 RBAC」缺口中的“用户管理 CRUD”改为已实现证据，并更新 feature-parity「用户管理」为 `Implementing` → `Implemented`（`Verified` 仍需完整双端真实栈与人工门禁）：

1. Release 构建 0 警告；Unit / Compatibility / Architecture / Integration 门槛同步上调且全绿。
2. SQL Server + MySQL 覆盖创建冲突、禁用、无权限、跨作用域拒绝、乐观并发。
3. Vue/Layui 同场景 E2E 通过；real-stack 至少覆盖只读列表或禁用一条路径。
4. OpenAPI/客户端契约无未登记破坏；命名门禁与许可证检查通过。
5. `git diff --check` 清洁；状态矩阵与对标矩阵已更新。

---

### Task 1: 规格冻结与失败验收夹具

**Files:**
- Create or update：Identity 用户管理窄规格（若尚无独立 Spec，先在本计划附录固化验收表，后续任务再升格 Spec）
- Test: Integration/API 失败夹具骨架
- Modify: `docs/roadmap/adminnet-feature-parity.md`（用户管理 → Designing/Implementing）

1. [ ] 写出验收表：端点、权限码、状态码、ProblemDetails code、双库约束名。
2. [ ] 先提交失败的 API/集成测试（列表未授权 403、创建重复用户名冲突、禁用后登录拒绝）。
3. [ ] 确认不扩大到角色/菜单表结构。

### Task 2: 双库持久化与领域命令

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/**`
- Add: SQL Server/MySQL paired migrations（仅本切片所需列/索引/约束）
- Test: `tests/Full.NET.IntegrationTests/Identity/**`

1. [ ] 迁移成对、可恢复；禁止无门禁破坏性 DDL。
2. [ ] Command/Query + FluentValidation；事务内审计；需要时写 Outbox。
3. [ ] 唯一约束冲突映射为稳定业务/冲突错误码（避免裸异常）。
4. [ ] 双库 Integration 全绿后再动 UI。

### Task 3: HTTP 端点与授权目录

**Files:**
- Modify: Identity Endpoints / Authorization catalog
- Test: Architecture（权限码注册）、Compatibility（若影响包络）

1. [ ] 标准 HTTP + ProblemDetails；精确权限，不靠前端隐藏。
2. [ ] 超级管理员仍走动态投影，不得通配旁路。
3. [ ] 更新 OpenAPI/契约夹具。

### Task 4: Vue / Layui 双端页面

**Files:**
- Modify: `ui/admin/**`、`ui/admin-layui/**`
- Modify: `packages/client-contracts/**`（若抽取纯函数）
- Test: 两端单测 + `pnpm test:e2e` 同场景

1. [ ] 列表、详情/表单、禁用确认；导航白名单映射本地组件。
2. [ ] 两端场景名与断言一致；未知组件拒绝保持。
3. [ ] 禁止单端先合入里程碑。

### Task 5: 真实栈冒烟与文档

**Files:**
- Modify: `tests/e2e/admin-real-stack/**`（最小新增）
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/verification/**`（本切片验证记录）

1. [ ] real-stack 增加用户列表或禁用冒烟（SQL Server；MySQL 在 main/发布门禁）。
2. [ ] 更新门槛数字与能力矩阵证据列。
3. [ ] 记录未做项（角色/菜单）防止宣传漂移。

---

## 停止条件

- 需要租户用户模型或打破 HostOnly 认证边界时：暂停并开独立 Spec/ADR。
- 发现必须先完成 Outbox 死信才能安全发领域事件时：先完成硬化 Task 6 最小死信，再继续本切片事件部分。
- 若后续书面改选“角色管理”或“菜单管理”为第一刀：更新本计划范围声明后重排 Task，不并行三刀（当前已确认用户管理优先，默认不适用）。

## 完成门禁

与 [`architecture-hardening`](2026-07-18-architecture-hardening.md) 完成门禁相同：新鲜测试、门槛同步、矩阵更新、规则/Skill 复盘、`git diff --check`。
