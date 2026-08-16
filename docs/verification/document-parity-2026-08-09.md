# Document Admin.NET Parity 验证（2026-08-09 计划收口）

> 更新时间：2026-08-16。本记录只陈述已执行命令与新鲜输出；不标记 `Production-verified`。

## 范围

- 分享安全：`TryConsumeAccess` Version 乐观锁 + 20 路并发矩阵
- 后端对称矩阵：recycle-bin / permissions / shares / statistics
- Vue 四页：recycle-bin、shares、permissions、statistics
- 调用点门禁：`api/me.ts`、`validate-vue-api-call-site-coverage.mjs`

## 集成测试（Release）

```text
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release \
  --filter "FullyQualifiedName~Document_share_concurrency|FullyQualifiedName~Document_admin_net_parity"
```

结果（2026-08-16 本地）：**4/4 通过**（MySQL + SQL Server 各 concurrency + parity）。

相关修复：

- `HostRecycleBinManagementService` 恢复后改读活跃文档，不再查询已删除快照
- `DocumentItemSql` statistics 语句拆分 MySQL `IFNULL` / `UTC_DATE()` 变体

## 前端

```text
pnpm exec vitest run \
  ui/admin/src/views/DocumentRecycleBinView.test.ts \
  ui/admin/src/views/DocumentSharesView.test.ts \
  ui/admin/src/views/DocumentStatisticsView.test.ts \
  ui/admin/src/views/DocumentPermissionsView.test.ts
```

结果：**7/7 通过**。

```text
pnpm test:openapi
```

结果：契约覆盖 + 调用点门禁通过（含 `identity-me-v1.json`、`menus.ts` `request<unknown>` 守卫）。

## 能力状态

- [`capability-status.md`](../roadmap/capability-status.md) Document 行 → **Build-verified**
- P2 #2 Vue 调用点覆盖 → **已完成**
- 仍禁止 `DeliveryCutover:Enabled=true` 与 `Production-verified`

## 未执行

- 专用环境 k6 1/2/4/8 容量矩阵（`Capacity-not-verified`）
- Playwright admin-parity 全量 E2E / WCAG 零违规复验（本轮未重跑）
