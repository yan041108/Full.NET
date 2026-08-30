# Workflow 定义只读与实例发起验证

## 范围

- 基线提交：`9b45390db4fc1ee2b4af0059ce4e2f529635ba45`
- 任务快照：`workflow-definition-start-20260830`
- 交付范围：定义只读列表、发布版本列表、发布表单版本读取、静态初始表单和幂等实例发起。
- 不在本切片：定义/表单设计器、草稿编辑、发布、实例取消/恢复和执行轨迹页。

## 边界

- Vue 只调用生成的 `workflowListDefinitions`、`workflowListDefinitionVersions`、`workflowGetFormVersion` 与 `workflowStartInstance` Operation。
- 冻结 `formSchemaJson` 必须通过 JSON 解析、schemaVersion/adapterVersion 一致性和静态字段目录 guard，未知协议失败关闭。
- 页面由 `workflow.definitions.read` 导航权限保护；发起入口另由 `workflow.instances.start` 控制，缺少操作权限时按钮不进入 DOM。
- 发起请求只提交静态表单 patch、业务类型、业务标识与浏览器安全随机幂等键。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| SQL Server/MySQL 客户端 OpenAPI 运行时导出 | 两侧各 1/1，通过且规范一致 |
| `pnpm test:openapi` | 118/118 通过 |
| `pnpm --filter @fullnet/client-contracts test` | 140/140 通过 |
| `pnpm --filter @fullnet/client-contracts build` | 通过 |
| `pnpm --filter @fullnet/admin test` | 507/507 通过 |
| `pnpm --filter @fullnet/admin build` | typecheck 与 Vite production build 通过 |
| `pnpm --filter @fullnet/admin test -- UsersView.test.ts` | 隔离重跑 24/24 通过；并行高负载时的超时未复现 |

## 包体状态

定义页保持动态导入，独立页面 chunk 约 `6.50 kB` minified、`2.39 kB` gzip；共享静态表单渲染器 chunk 约 `4.81 kB` minified、`1.80 kB` gzip。首屏静态图从上一切片的 `931012/273747` 增至 `938048/275097` bytes，本切片增量为 `7036/1350` bytes，分别约为历史基线的 `1.20%/0.70%`，处在单切片 5% 变化带内。

全局门禁仍因跨模块累计漂移超过 2026-07-27 历史基线 `585792/193619` 而失败；本任务不抬高基线，继续标记 `Capacity-not-verified`，由独立首屏治理任务关闭累计债务。

## 规则与 Skill 演进

未发现新的规则冲突或项目 Skill 缺口，不更新规则与 Skill 候选。
