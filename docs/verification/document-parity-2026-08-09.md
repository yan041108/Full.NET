# Document Admin.NET Parity 验证（2026-08-09 计划收口）

> 更新时间：2026-08-16。本记录只陈述已执行命令与新鲜输出；不标记 `Production-verified`。

## 范围

- 分享安全：`TryConsumeAccess` Version 乐观锁 + 20 路并发矩阵 + 匿名分享 RateLimit（429 + `hosting.rate_limit.exceeded`）
- 后端对称矩阵：recycle-bin / permissions / shares / statistics（含 `TodayDownloadCount` 独立 SQL）
- Vue 页面：host-items（版本历史 + MVP 预览）、recycle-bin、shares、permissions、statistics
- 契约：`document-host-items-v1.json` 增补 `GET .../versions`、`GET .../preview`；`ui/admin` API 模块统一为 `host-document-*`
- 调用点门禁：`api/me.ts`、`validate-vue-api-call-site-coverage.mjs`
- E2E/WCAG：admin-parity axe 0 violations；admin-real-stack Document 四页 spec（SQL Server + MySQL 项目）

## 集成测试（Release）

```text
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release \
  --filter "FullyQualifiedName~DocumentApi|FullyQualifiedName~Document_share_rate_limit"
```

结果（2026-08-16 本地）：Document API 矩阵 + 分享限流双库通过；版本列表/预览断言在 `DocumentHostItemAssertions` 中执行。

## 前端

```text
pnpm exec vitest run ui/admin/src/views/HostDocumentItemsView.test.ts \
  ui/admin/src/views/DocumentRecycleBinView.test.ts \
  ui/admin/src/views/DocumentSharesView.test.ts \
  ui/admin/src/views/DocumentStatisticsView.test.ts \
  ui/admin/src/views/DocumentPermissionsView.test.ts
```

```text
pnpm test:e2e:admin
pnpm test:e2e:real
pnpm test:e2e:real:mysql
```

结果（2026-08-16）：`accessibility-i18n.spec.mjs` 中认证壳层/匿名 WCAG 用例通过；Document 五页扫描与部分壳层交互用例仍待清零（见 `tests/e2e/admin-parity/tests/accessibility-i18n.spec.mjs` 最新输出）。admin-real-stack Document 四页 spec 已添加，需在真实栈环境执行上述 real 命令完成双库复验。

## Admin.NET 有意差异

- **Office 在线转 PDF 预览**：Admin.NET 插件能力；Full.NET 1.0 仅交付 text/image/PDF inline 预览 MVP，非白名单 MIME 返回 `document.host_document.preview_not_supported`（422），不阻塞 Verified。

## 能力状态

- [`capability-status.md`](../roadmap/capability-status.md) Document 行 → **Build-verified**（Verified 升档待 E2E/WCAG fresh 输出）
- 大型插件队列 #1（Document）关闭；下一队列项为 Workflow（见 [`adminnet-feature-parity.md`](../roadmap/adminnet-feature-parity.md) §4.1）
- 仍禁止 `DeliveryCutover:Enabled=true` 与 `Production-verified`

## 未执行

- 专用环境 k6 1/2/4/8 容量矩阵（`Capacity-not-verified`）
