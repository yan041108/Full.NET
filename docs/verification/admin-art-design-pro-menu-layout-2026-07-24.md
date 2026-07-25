# Art Design Pro 菜单布局双端验证记录

- 日期：2026-07-24（持续更新至 2026-07-25）
- 切片：Vue Art 壳层与 Layui 对等壳层（布局、设置、标签页、面包屑、顶栏控件）

## 交付范围

| 层级 | 内容 |
|---|---|
| Vue | `ArtAdminShell` / `ArtSettingsPanel` / `ArtTopBar` / `ArtChatDrawer` |
| Layui | `shell-art-settings` / `shell-settings` / `shell-layout` / `shell-tabs` / `shell-chrome` / `shell-topbar` / `shell-global-search` / `shell-notification-panel` / `shell-chat-drawer` |
| E2E | `menu-layout.spec.mjs`（Layui **4** 项 + 双端 **5** 项）；`shell-parity.spec.mjs` |

## 本地验证

| 命令 | 结果 |
|---|---|
| `pnpm --filter @fullnet/admin-layui test` | **79/79** 通过 |
| `pnpm --filter @fullnet/admin test` | **133/133** 通过 |
| `menu-layout.spec.mjs` Layui 专属 | **4/4** 通过 |
| `menu-layout.spec.mjs` 双端壳层 | **5/5** 通过（Vue + Layui 各跑一遍） |
| `shell-parity.spec.mjs` | **34/34** 通过 |

完整迁移验收见 [`admin-art-design-pro.md`](admin-art-design-pro.md)。

## Layui 壳层对等状态

| 能力 | 状态 |
|---|---|
| 四种菜单布局 + `dualMenuShowText` | 已实现 |
| 完整设置抽屉（主题/颜色/容器等） | 已实现 |
| 多标签页 + 面包屑 | 已实现 |
| 折叠侧栏 / 刷新 / 全屏 | 已实现 |
| 全局搜索 / 主题切换 | 已实现 |
| 顶栏通知面板 | 已实现（演示数据） |
| 顶栏聊天抽屉 | 已实现（演示数据） |
