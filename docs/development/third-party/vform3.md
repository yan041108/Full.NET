# VForm3 来源与采用记录

- **状态：** 已按项目所有者 2026-08-30 明确决策采用
- **采用范围：** Vue 管理后台通用表单设计态；Workflow 是首个消费方
- **精确包版本：** `vform3-builds@3.0.10`
- **官方仓库：** <https://github.com/vform666/variant-form3-vite>
- **上游提交：** `c67479e496bab56a93a3dff168a4f529d8293c67`
- **NPM integrity：** `sha512-sULNk72Z6NG94X9MMgpBwinaIOYjtl7Bn5jZbV9iZaglSYr2SHm/wpXPAjl8F8OW6SzOOJgIpRek+NvOvN7wsg==`
- **包归档 SHA-256：** `8C65FACBEF36DA9D1333634E6EEE007B15D74815E167D7838C3C7E06AC849539`
- **许可证：** 上游 `Variant Form 许可条款 1.0`，自定义许可，不是 MIT
- **历史 PoC：** [2026-08-30 VForm3 兼容与安全 PoC](../../verification/2026-08-30-workflow-designer-dependency-poc.md)

## 采用裁决

历史 PoC 的 No-Go 已被项目所有者“`vform3` 直接使用”的明确决策覆盖。依赖已精确锁定并进入管理端发布物，`THIRD-PARTY-NOTICES` 同步登记；不得把 VForm3 标记为 Full.NET 自有 MIT 源码。

该决策不改变以下技术事实：包 metadata 没有 `license` 字段、归档未携带 LICENSE，生产构建会报告 direct `eval`，包内也包含 `new Function`、脚本/CSS 编辑和远程资源能力。因此许可证和安全风险必须持续可见，禁止把历史 PoC 改写为“风险已消失”。

## Full.NET 封装边界

- `packages/admin-form-designer` 是独立 workspace 模块，只封装通用 Host、延迟加载、生命周期和 VForm3 所需 Element Plus 组件的选择性注册，不依赖 Workflow 契约。
- Workflow 通过 `VForm3WorkflowDesigner.vue` 和 `vform3-adapter.ts` 适配；其他业务场景必须建立自己的闭合 Adapter，禁止共享 Workflow Schema。
- VForm3 主体延迟加载；直接 UMD 集成产生的共享运行时增量已经 A/B 量化并接受。首屏静态图、VForm3 大体积延迟块和选择性 Element Plus 运行时图分别接受预算。
- VForm3 JSON 仅是客户端设计态，不是公共 API、数据库协议或运行时授权依据。
- 保存前必须经 `vform3-adapter.ts` 转换为闭合 `WorkflowFormSchema`。
- 只有服务端组件目录同时声明 Designable、Publishable、Executable 的字段类型可以保存。
- 脚本、生命周期函数、HTML/iframe、CSS、自定义扩展、远程 URL/Header/Body、文件、图片、富文本和未知组件失败关闭。
- 不增加 `unsafe-eval`，不放宽现有 CSP；严格 CSP 下第三方包的危险路径不可用。
- H5、微信小程序和支付宝小程序不得安装 VForm3 或 Element Plus。

## 验证要求

每次升级必须重新执行精确版本/哈希、许可证、漏洞、生产构建、包体、严格 CSP、静态危险能力扫描、适配器单测和真实浏览器保存测试。还必须检查 Element Plus 组件解析失败与弃用提示；不得使用 caret/range，不得通过抬高包体预算或放宽 CSP 掩盖回归。
