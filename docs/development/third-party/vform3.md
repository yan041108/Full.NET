# VForm3 来源与采用记录

- **状态：** 已按项目所有者 2026-08-30 明确决策采用
- **采用范围：** Vue 管理后台通用表单设计态；Workflow 是首个消费方
- **采用模型：** 基于 VForm3 3.0.10 JSON 与交互模型的仓库内 ESM 安全子集
- **官方仓库：** <https://github.com/vform666/variant-form3-vite>
- **上游提交：** `c67479e496bab56a93a3dff168a4f529d8293c67`
- **历史 NPM 包：** `vform3-builds@3.0.10`（已于 2026-09-04 从生产依赖图移除）
- **许可证：** 上游 `Variant Form 许可条款 1.0`，自定义许可，不是 MIT
- **历史 PoC：** [2026-08-30 VForm3 兼容与安全 PoC](../../verification/2026-08-30-workflow-designer-dependency-poc.md)

## 采用裁决

历史 PoC 的 No-Go 已被项目所有者采用 VForm3 并允许源码升级改造的明确决策覆盖。2026-09-04 起不再直接发布旧 UMD 包，而是保留兼容 JSON/交互模型并由当前工具链编译仓库内 ESM 安全子集；`THIRD-PARTY-NOTICES`、上游许可与作者声明同步保留。不得把该来源标记为 Full.NET 自有 MIT 源码。

旧包 metadata 没有 `license` 字段、归档未携带 LICENSE，且包含 direct `eval`、`new Function`、脚本/CSS 编辑和远程资源能力。ESM 安全子集已从生产依赖图物理排除这些能力，但自定义许可义务仍然存在，禁止把历史 PoC 改写为“VForm3 从未存在风险”。

## Full.NET 封装边界

- `packages/admin-form-designer` 是独立 workspace 模块，封装通用 Host、延迟加载、VForm3 兼容 ESM 状态与安全组件目录，不依赖 Workflow 契约。
- Workflow 通过 `VForm3WorkflowDesigner.vue` 和 `vform3-adapter.ts` 适配；其他业务场景必须建立自己的闭合 Adapter，禁止共享 Workflow Schema。
- ESM 设计器保持延迟加载；其 JavaScript 独立块和首屏静态图分别设定包体预算，不再保留旧 UMD 与选择性 Element Plus 运行时图预算。
- VForm3 JSON 仅是客户端设计态，不是公共 API、数据库协议或运行时授权依据。
- 保存前必须经 `vform3-adapter.ts` 转换为闭合 `WorkflowFormSchema`。
- 只有服务端组件目录同时声明 Designable、Publishable、Executable 的字段类型可以保存。
- 脚本、生命周期函数、HTML/iframe、CSS、自定义扩展、远程 URL/Header/Body、文件、图片、富文本和未知组件不进入目录，保存 Adapter 仍二次失败关闭。
- 不增加 `unsafe-eval`，不放宽现有 CSP；严格 CSP 下第三方包的危险路径不可用。
- H5、微信小程序和支付宝小程序不得安装 VForm3 或 Element Plus。

## 验证要求

每次升级必须重新执行精确上游提交、许可证、生产构建、包体、严格 CSP、静态危险能力扫描、适配器单测和真实浏览器保存测试。不得使用浮动上游引用，不得通过抬高包体预算或放宽 CSP 掩盖回归。
