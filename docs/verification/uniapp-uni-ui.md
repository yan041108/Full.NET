# uni-app uni-ui 引入验证记录

- 验证时间：2026-07-26 19:24（Asia/Shanghai）
- 分支：`codex/uniapp-uni-ui-adoption`
- 状态：`Implementing / Build-verified`

## 交付范围

- 精确依赖 `@dcloudio/uni-ui@1.5.12`，通过 easycom 唯一映射解析 `uni-*`；默认依赖不含 uView。
- `uni.scss` 映射 Full.NET 主色、成功、警告、错误、文字、边框与圆角；业务页面不直接覆盖 `.uni-*` 内部选择器。
- Development/Test 冒烟页覆盖 `uni-section`、`uni-list`、`uni-list-item`、`uni-forms`、`uni-easyinput` 与 `uni-popup`。
- 语言设置页使用 uni-ui 列表、表单与反馈组件，但继续复用原有 Locale Settings Model、Vue I18n、ProblemDetails 与 `uni.request` 适配。
- Locale Settings Model 在控制器发布 busy 快照前建立本地提交门禁，避免同一选择被并发提交两次。

## 自动验证

| 验证 | 结果 |
|---|---|
| uni-app 单测 | **103/103** |
| `vue-tsc --noEmit` | 通过 |
| H5 Edge E2E | **6/6** |
| H5 构建 | **11 files / 437931 bytes** |
| 微信小程序构建 | **62 files / 252662 bytes** |
| 支付宝小程序构建 | **63 files / 369398 bytes** |
| uView 产物扫描 | **0 命中** |
| 客户端漏洞门禁 | 无未登记 Critical/High |
| 生产许可证枚举 | **13** 个许可证分组；uni-ui 为 Apache-2.0 |

H5 新增场景验证中文/英文组件文案、输入焦点、必填错误、弹层开关和 320 CSS px 无横向溢出。既有场景继续验证匿名与认证语言切换、保存失败回滚、409 ProblemDetails、`Accept-Language` 和生产产物不包含 DEV bridge。

## 包体积

相对引入前记录，原始构建目录由 H5 211182 B、微信 118355 B、支付宝 225966 B 增至本次表中数值。增长来自所选 uni-ui 组件及其样式，三个目标均保持在当前客户端基础阶段的可接受范围；后续继续按页面实际使用 easycom 组件，禁止引入第二套全量 UI 库。

## 未验证边界

微信开发者工具与支付宝小程序开发者工具在 PATH、Program Files 和当前用户 LocalAppData 常见路径均未找到，因此未执行导入、模拟器或真机验证。真实登录、租户选择、会话失效与平台发布清单也不在本切片内。上述证据补齐前不得标记为 `Verified`。

构建会显示来自 DCloud/uni-ui 1.5.12 的 Sass legacy JS API 与全局 `mix()` 弃用警告；Full.NET 自有 SCSS 已使用 `@use`，没有修改第三方源码。
