# Workflow 实例详情与执行轨迹验证

## 范围

- 基线提交：`21d64baedbef24d453a91434826888545a2e0db2`
- 任务快照：`workflow-instance-timeline-20260830`
- 交付范围：按实例 UUID 查询只读概要、当前状态、活动待办和服务端顺序执行轨迹。
- 不在本切片：实例列表、取消、恢复、改派和任何后端状态转换。

## 边界

- Vue 只通过生成的 `workflowGetInstance` 与 `workflowListInstanceExecutionLogs` Operation 读取数据，不手写路径或响应断言。
- 页面由 `workflow.instances.read` 导航权限保护；服务端继续在两个 Endpoint 上执行同一精确权限。查询返回 `403/404` 时清除旧实例并展示 ProblemDetails，避免残留敏感数据。
- 执行日志按服务端审计顺序渲染，不由客户端重排；所有状态、转换键、UUID 和时间均作为纯文本展示。
- 页面不创建 `workflow.instances.cancel` 或 `workflow.instances.recover` 操作入口。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| SQL Server/MySQL 客户端 OpenAPI 运行时导出 | 两侧各 1/1，通过且规范一致 |
| `pnpm test:openapi` | 118/118 通过 |
| `pnpm --filter @fullnet/client-contracts test` | 140/140 通过 |
| `pnpm --filter @fullnet/client-contracts build` | 通过 |
| `pnpm --filter @fullnet/admin test` | 510/510 通过 |
| `pnpm --filter @fullnet/admin build` | typecheck 与 Vite production build 通过 |
| `pnpm test:localization` | 7/7 通过 |
| `pnpm test:governance` | 52/52 通过 |
| `pnpm test:naming` | 30/30 通过 |
| `pnpm audit:clients` | 无未审查 Critical/High 漏洞 |
| `pnpm test:inner -- --snapshot workflow-instance-timeline-20260830` | 影响选择器确认无 Integration 目标 |

## 包体状态

实例页保持动态导入，独立 JavaScript chunk 约 `5.39 kB` minified、`1.97 kB` gzip，独立 CSS chunk 约 `5.16 kB` minified、`1.22 kB` gzip。首屏静态图从上一切片的 `938048/275097` 增至 `942509/276148` bytes，本切片增量为 `4461/1051` bytes，分别约为历史基线的 `0.76%/0.54%`，处在单切片 5% 变化带内。

全局 `pnpm test:bundle-budgets` 仍因跨模块累计漂移超过 2026-07-27 历史基线 `585792/193619` 而失败；本任务不抬高基线，继续标记 `Capacity-not-verified`，由独立首屏治理任务关闭累计债务。

## 规则与 Skill 演进

未发现新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
