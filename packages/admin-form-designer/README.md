# Full.NET Admin Form Designer

`@fullnet/admin-form-designer` 是管理端可复用的表单设计器宿主，不属于 Workflow 模块。

它只负责延迟加载 VForm3、按 Vue App 隔离插件安装状态、选择性注册设计器依赖的 Element Plus 组件，并提供 `getFormJson` / `setFormJson` 通用接口。各业务场景必须在自己的 Adapter 中把第三方 JSON 转换为本领域的闭合契约；本包不定义 Workflow、审批或其他业务语义。

## 使用边界

- 只允许 Vue 管理端设计态使用，不进入 Host.Api、Worker、移动端或公共协议。
- 业务模块不得把 VForm3 JSON 直接保存为权威业务模型。
- 新场景必须自行限制组件目录、脚本、远程资源和可执行扩展能力。
- 禁止改成全量 `app.use(ElementPlus)`；新增 VForm3 组件依赖必须先由真实浏览器证明，再更新选择性注册清单和运行时图预算。
- `vform3-builds` 必须保持精确版本，并持续接受许可证、CSP、漏洞和延迟块包体审计。
