# Art Design Pro 上游来源清单

| 字段 | 值 |
|---|---|
| 仓库 | https://github.com/Daymychen/art-design-pro |
| 固定提交 | `f3aaf58eec1a0e988f162352c33862327a484f95` |
| 许可证 | MIT |
| 复核日期 | 2026-07-24 |
| Full.NET 落点 | `ui/admin/src/framework/art-design/` |

## 迁入范围（选择性源码）

| 上游路径 | Full.NET 目标 | 修改摘要 |
|---|---|---|
| `src/plugins/echarts.ts` | `charts/echarts.ts` | 仅注册 Line/Bar/Pie 与必要组件 |
| `src/components/core/charts/art-line-chart/index.vue`（交互节选） | `charts/FullNetChart.vue` | 去除 Store；增加无障碍摘要表与空/错状态 |
| `src/views/index/index.vue` | `layout/ArtAdminShell.vue` | 去除 Pinia/全局组件；改为 Adapter 驱动 |
| `src/views/index/style.scss` | `theme/art-layout.css` | 去除 Tailwind 依赖；映射 Full.NET 设计令牌 |
| `src/components/core/layouts/art-menus/art-sidebar-menu/index.vue` | `layout/ArtSidebar.vue` | 使用服务端导航白名单；Element Plus Icons 替代 Iconify |
| `src/components/core/layouts/art-header-bar/index.vue` | `layout/ArtTopBar.vue` | 保留搜索/语言/用户区；租户切换委托 Session |
| `src/components/core/layouts/art-notification/index.vue`（节选） | `layout/ArtNotificationPanel.vue` | 顶栏通知面板；演示数据 |
| `src/components/core/layouts/art-chat-window/index.vue`（节选） | `layout/ArtChatDrawer.vue` | 顶栏聊天抽屉；演示消息 |
| `src/components/core/layouts/art-breadcrumb/index.vue` | `layout/ArtBreadcrumb.vue` | 基于 Flat 导航生成面包屑 |
| `src/components/core/layouts/art-work-tab/index.vue` | `layout/ArtTabs.vue` | 简化标签页；不引入 worktab Store |
| `src/components/core/layouts/art-global-search/index.vue` | `layout/ArtGlobalSearch.vue` | 白名单导航搜索；Cmd/Ctrl+K |
| `src/components/core/layouts/art-settings-panel/index.vue`（节选） | `layout/ArtSettingsPanel.vue` | 主题与菜单布局（左/顶/混合/双栏）；`dualMenuShowText` |
| `src/components/core/layouts/art-menus/art-horizontal-menu/index.vue`（节选） | `layout/ArtHorizontalMenu.vue` | 顶部水平菜单；Adapter 白名单导航 |
| `src/components/core/layouts/art-menus/art-mixed-menu/index.vue`（节选） | `layout/ArtMixedMenu.vue` | 顶部分组 + 侧栏二级 |
| `src/components/core/layouts/art-menus/art-dual-menu/index.vue`（节选） | `layout/ArtDualMenuRail.vue` | 双栏一级轨 + 侧栏二级；可选显示文字 |
| `src/views/index/style.scss`（菜单布局节选） | `theme/art-menu-layouts.css` | 四种 `menuLayout` 壳层样式；移动端回退左侧抽屉 |
| `src/views/auth/login/index.vue` | `views/LoginView.vue`、`auth/ArtLoginLeftPanel.vue`、`auth/art-login.css` | 左右分栏 + ElForm；去除拖拽验证与演示账号 |
| `src/assets/styles/core/el-ui.scss`（节选） | `theme/art-theme.css` | Element Plus 视觉覆盖；`--el-color-primary` 高对比度 |

## 明确排除

- `axios`、模板 Mock API、演示路由与演示数据
- `pinia-plugin-persistedstate` 与 Access Token 持久化
- `@wangeditor/editor`、`xlsx`、`xgplayer`、`crypto-js` 等未命中模块依赖
- Tailwind CSS、Iconify、`ArtSvgIcon` 运行时图标体系
- 动态 `import()` 业务组件路径与模板认证流程
- 演示图片、品牌 Logo 与未审计字体资产

## Full.NET 保留权威

- `packages/client-contracts` 协议与 ProblemDetails
- `ui/admin/src/api/http.ts`、`auth/session.ts`、`navigation/catalog.ts`
- 内存 Access Token、HttpOnly Refresh Cookie、CSRF 与精确 CORS
- Vue/Layui 双端 E2E 与同场景契约
