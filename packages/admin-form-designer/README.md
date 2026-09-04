# Full.NET Admin Form Designer

`@fullnet/admin-form-designer` 是管理端可复用的表单设计器宿主，不属于 Workflow 模块。

它只负责延迟加载基于 VForm3 3.0.10 JSON/交互模型重写的仓库内 ESM 安全子集，并提供 `getFormJson` / `setFormJson` 通用接口。各业务场景必须在自己的 Adapter 中把设计态 JSON 转换为本领域的闭合契约；本包不定义 Workflow、审批或其他业务语义。

## 使用边界

- 只允许 Vue 管理端设计态使用，不进入 Host.Api、Worker、移动端或公共协议。
- 业务模块不得把 VForm3 JSON 直接保存为权威业务模型。
- 新场景必须自行限制组件目录；脚本、远程资源、HTML/CSS 和可执行扩展不得进入设计器依赖图。
- 不得重新引入旧 `vform3-builds` UMD、全量 `app.use(ElementPlus)` 或运行时远程组件；扩大目录必须先建立 Adapter 白名单和真实浏览器回归。
- 上游来源固定到提交 `c67479e496bab56a93a3dff168a4f529d8293c67`，自定义许可与作者声明必须持续保留，详见 `vendor/vform3/PROVENANCE.md`。
