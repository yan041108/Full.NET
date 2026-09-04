# Workflow Tenant 本地收件人候选目录验证记录

> 日期：2026-09-05
> 状态：`Build-verified`；SQL Server/MySQL 共享 API 断言和 Linux Native AOT 以目标提交的 GitHub Actions 结果为最终门禁。
> 任务基线：`1f5107fcfc91f25207a136ee4348d4dae4580930`

## 1. 交付边界

本切片关闭工作流设计器在 Tenant 上下文仍枚举全部 Host 活动用户的边界缺口。Host 定义继续读取活动 Host 用户；Tenant 定义只读取当前可信租户的活动直属用户，以及被授予当前 Tenant 活动角色的 Host 用户。

候选接口路径、分页 JSON、权限码和 OpenAPI operationId 均未改变，客户端不提交 TenantId。TenantId 只由 `ICurrentTenant` 和 Dapper `CurrentTenantId` 绑定提供；Workflow 只调用 Identity 的最小只读 Contract，不读取 Identity 或 Organization 表。

## 2. 发布与数据边界

- 候选分页、总数和按 Id 批量读取均使用 Identity 自有 SQL；SQL Server/MySQL 分页语句成对实现，并按规范化用户名和用户 Id 稳定排序。
- Tenant 查询要求用户活动；直属 Tenant 用户以 `TenantId + ScopeKey` 证明归属，Host 用户则必须额外拥有当前 Tenant 的活动角色。仅 Host 用户、其他 Tenant 用户和停用角色关系不会成为候选人。
- `notify.cc` 发布前按定义作用域一次批量复验所有去重收件人；任何收件人无效都返回既有稳定错误 `workflow.definition.cc_recipients_invalid`，校验仍发生在 Workflow 本地写事务之前。
- Host 批量复验保持活动 Host 用户语义；新增 Global SQL 已登记到精确治理目录，禁止全局查询静默扩张。
- 未新增表、迁移、公共 HTTP DTO 或客户端生产代码，`ui/admin-layui` 零修改。

## 3. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| Workflow/Identity 聚焦 Unit | 167 项通过，0 失败 |
| Architecture 全量 | 200 项通过，0 失败 |
| Workflow 定义 API 与 Workflow-Vue3 设计器 Vitest | 9 项通过，0 失败 |
| `dotnet build Full.NET.slnx --no-restore -c Release` | 通过，0 警告、0 错误 |
| Integration 项目 `--no-restore` 构建 | 通过，0 警告、0 错误 |
| 受影响 Integration slice 计划 | 精确选择 Identity、Workflow 和 Workflow 共享 API 断言；真实双库执行交给 Actions |
| `git diff --check` / Layui 影响 | 无空白错误；Layui 零修改 |

共享 API 验收在 SQL Server/MySQL 两个 Workflow 测试类中复用：当前 Tenant 用户会出现在候选分页并可发布为抄送人，不具备当前 Tenant 角色的 Host 用户不会出现且无法发布。该环境重型断言不在本机用容器替代 Actions 门禁。

## 4. 保留边界

Workflow 继续保持 `Build-verified`，不得提升为 `Verified`。以下能力仍开放：

- Worker 恢复、租约、重放与 reconcile；
- Workflow 到 Notifications 的异步业务提醒投影；
- 角色/组织负责人审批、会签/或签、转办、加签和更复杂网关；
- 人工产品验收、生产等价容量与故障演练。

规则演进结论：本轮没有新的用户纠正、重复失败、高风险类别或规则冲突，不更新规则候选。Skill 演进结论：既有 `fullnet-module-delivery` 已完整覆盖本切片，没有形成新的独立 Skill 缺口。
