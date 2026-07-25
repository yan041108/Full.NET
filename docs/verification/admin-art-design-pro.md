# Art Design Pro 管理壳层迁移验收记录

- 日期：2026-07-25
- 切片：Vue Art 壳层迁入 + Layui 对等壳层
- 关联计划：[`2026-07-18-vue-art-design-pro-adoption.md`](../superpowers/plans/2026-07-18-vue-art-design-pro-adoption.md)
- 菜单布局细项：[`admin-art-design-pro-menu-layout-2026-07-24.md`](admin-art-design-pro-menu-layout-2026-07-24.md)

## 交付范围

| 层级 | 内容 |
|---|---|
| Vue | `ui/admin/src/framework/art-design/`（布局、设置、搜索、通知、聊天、图表） |
| Layui | `ui/admin-layui/js/core/shell-*.js` 对等控制器 |
| 上游清单 | [`docs/upstreams/art-design-pro.md`](../upstreams/art-design-pro.md) |
| 边界测试 | `ui/admin/tests/art-design-boundary.test.mjs` |

## 本地验证（2026-07-25）

| 命令 | 结果 |
|---|---|
| `node --test ui/admin/tests/art-design-boundary.test.mjs` | **2/2** 通过 |
| `pnpm --filter @fullnet/admin test` | **133/133** 通过 |
| `pnpm --filter @fullnet/admin-layui test` | **79/79** 通过 |
| `pnpm exec playwright test tests/menu-layout.spec.mjs` | **14/14** 执行通过（6 项跨端互斥 skip） |
| `pnpm exec playwright test tests/shell-parity.spec.mjs` | **34/34** 通过 |
| `pnpm exec playwright test tests/accessibility-i18n.spec.mjs` | 见既有 a11y 记录（Vue 键盘/320px 已覆盖） |

双端壳层 E2E（`menu-layout.spec.mjs`）覆盖：四种菜单布局（Vue/Layui 各 1）、Layui 设置/标签/顶栏专属 5 项、双端全局搜索与主题、通知面板、聊天抽屉、多标签页。

## 保留的 Full.NET 权威边界

- `packages/client-contracts`、`ui/admin/src/api/http.ts`、`auth/session.ts`、`navigation/catalog.ts`
- 内存 Access Token、HttpOnly Refresh Cookie、CSRF、ProblemDetails、导航白名单
- 禁止 `axios`、持久化 Token、模板 Mock API、wangEditor 等（边界测试强制）

## 未纳入本切片 / 不得提前标 Verified

| 项 | 说明 |
|---|---|
| Tiptap 富文本 | 独立计划 [`2026-07-18-rich-text-editor-foundation.md`](../superpowers/plans/2026-07-18-rich-text-editor-foundation.md) |
| 通知/聊天真实 API | 当前为壳层演示数据，与上游 Art 模板一致 |
| NVDA / 200% 缩放 / 强制颜色 | 须人工验收，见 `getting-started.md` |
| 完整业务后台 | C2 模块对标仍在 `Implementing` |

## 状态结论

Vue 与 Layui 管理壳层 Art Design Pro 对等切片已达 **`Build-verified`**：自动化与双端 E2E 证据齐全，但尚未满足 `Verified` 所需的人工辅助技术与真实发布编排补证。
