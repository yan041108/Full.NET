# Workflow 角色与组织办理人解析验证记录

> 日期：2026-09-05  
> 状态：`Build-verified`；OpenAPI 客户端快照与双库 Integration 以目标提交 GitHub Actions 为最终门禁。  
> 任务基线：`8a7961ffd6f1e3e8566d893e9d8081ea861f3efb`

## 1. 交付边界

本切片为人工审批节点增加闭合办理人解析策略 `assigneePolicy.sources[]`，支持：

- `specified_users`：指定用户
- `role_members`：角色成员
- `organization_unit_leader`：机构负责人（职级最高成员）
- `initiator`：流程发起人（默认）
- `initiator_primary_unit_leader`：发起人主部门负责人

Workflow 仅通过 Identity/Organization 最小 Contract Port 批量解析；Host 作用域禁止机构类 resolver。发布时校验实体引用，启动与推进时按发起人复验并快照办理人。

## 2. 本地新鲜证据

| 验证 | 结果 |
| --- | --- |
| Workflow 聚焦 Unit | 248 项通过，0 失败 |
| `dotnet build` Workflow/Identity/Organization/Host.Api | 通过 |
| Workflow-Vue3 适配器 Vitest（含 assigneePolicy） | 待本机 `pnpm --filter @fullnet/admin test` 执行 |

## 3. 保留边界

- OpenAPI 正式客户端生成待快照脚本恢复后补齐 `role-candidates` / `organization-unit-candidates`；当前 Admin 通过 `http.request` 直连新端点。
- 页面真实栈 E2E、双库 Integration、Linux Native AOT 与人工验收未在本切片执行。

规则演进结论：未命中规则升级候选。Skill 演进结论：沿用 `fullnet-module-delivery`，无新 Skill 缺口。
