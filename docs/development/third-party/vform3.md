# VForm3 来源与采用记录

- **状态：** `No-Go`；未进入 Full.NET 产品依赖或发布物
- **复核日期：** 2026-08-30
- **官方仓库：** <https://github.com/vform666/variant-form3-vite>
- **上游提交：** `c67479e496bab56a93a3dff168a4f529d8293c67`
- **候选包：** `vform3-builds@3.0.10`，必须精确锁定，禁止 caret/range
- **NPM integrity：** `sha512-sULNk72Z6NG94X9MMgpBwinaIOYjtl7Bn5jZbV9iZaglSYr2SHm/wpXPAjl8F8OW6SzOOJgIpRek+NvOvN7wsg==`
- **包归档 SHA-256：** `8C65FACBEF36DA9D1333634E6EEE007B15D74815E167D7838C3C7E06AC849539`
- **许可证：** 上游 `Variant Form 许可条款 1.0`，自定义许可，不是 MIT
- **证据：** [2026-08-30 VForm3 兼容与安全 PoC](../../verification/2026-08-30-workflow-designer-dependency-poc.md)

## 许可证与发布边界

上游条款允许个人或公司商业使用，并允许分发构建代码；分发源代码时要求保留作者声明。Full.NET 不得把 VForm3 标记为自有 MIT 源码。候选 NPM 包自身没有 `license` 元数据，也没有随包携带 LICENSE 文件，`pnpm licenses list --prod --json` 因此将其识别为 `Unknown`。在任何重新采用评审中，必须重新核对当时的上游条款、把许可证文本和来源纳入受控第三方证据，并在依赖实际进入发布物时更新 `THIRD-PARTY-NOTICES`。

当前没有安装产品依赖，因此本次不修改 `THIRD-PARTY-NOTICES` 和 `pnpm-lock.yaml`，避免把未发布的软件误记为已再分发组件。

## 原拟采用范围

候选能力仅限 Vue 管理后台 Workflow 表单 Draft 的设计态交互，以及由 Full.NET 安全 Schema 派生的受控 Web 预览。即使以后重新开放，也必须满足以下边界：

- VForm3 原始 JSON 不是公共 API、数据库权威协议或运行时授权依据。
- 服务端只接受允许的 Draft 子集并单向编译为不可变 `WorkflowFormSchema`。
- H5、微信小程序和支付宝小程序不得安装 VForm3 或 Element Plus。
- 禁止生命周期脚本、事件函数、任意 JavaScript、HTML/iframe、CSS、远程 URL、远程 Header/Body、文件/图片/富文本和未知组件。
- 设计器只能位于独立懒加载路由，不能进入管理后台首屏共享静态图。

## No-Go 原因

`vform3-builds@3.0.10` 可以在当前 Vue/Element Plus/TypeScript/Vite 组合下完成 typecheck、production build 和基本挂载，但候选发布包中直接包含 `eval`/`new Function` 以及脚本、CSS、远程资源装载路径；默认设计器还暴露 HTML、文件、图片、富文本、自定义扩展、全局 CSS/函数和事件代码编辑能力。严格 CSP 浏览器测试同时观察到默认模板图片访问外部域名。

这不是通过隐藏菜单即可关闭的边界：危险执行能力仍存在于进入浏览器的第三方产物中，与 Full.NET 的 CSP 和静态安全闭包不变量冲突。因此停止直接包接入，不允许通过增加 `unsafe-eval`、放宽远程资源策略或提高包体预算绕过。

## 重新开放条件

只有满足以下全部条件，才允许创建新的采用评审：

1. 上游提供可核验的 CSP-safe 构建，或单独批准一个可审计的 Full.NET 源码构建；构建过程必须物理移除动态代码执行、脚本/CSS 编辑、远程资源和禁用组件，而不是运行时隐藏。
2. 静态扫描对 `eval`、`new Function`、动态脚本注入和任意远程 URL 达到零命中，并由严格 CSP 浏览器 E2E 证明无需 `unsafe-eval`。
3. 精确版本、来源提交、构建补丁、包哈希、许可证文本、漏洞审计和传递/内嵌代码归属可以完整归档。
4. 独立懒加载后的 minified/gzip/Brotli 增量通过既有前端预算，不抬高预算掩盖增长。
5. 受限 Draft 必须经过服务端编译器失败关闭，客户端不能决定字段权限或提交替换后的 Form JSON。

## 更新流程

重新评估不得直接修改 `ui/admin/package.json`。先在仓库外隔离工作区复现本记录的 typecheck、production build、mount/unmount、render、CSP、键盘、静态扫描、许可证、漏洞与包体测试；证据通过后再提交精确锁定的依赖、锁文件、第三方声明和独立安全 Adapter。
