# Rich Text Editor Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为公告、站内通知等真实后台内容建立安全、可迁移的富文本基础，使用 Tiptap Core 同时支持 Vue 与 Layui 管理端，并由服务端统一执行 HTML 白名单净化。

**Architecture:** 编辑器能力分为三层：仓库级 JSON Profile 定义首期格式、链接、媒体和长度边界，TypeScript 与 C# 分别生成/映射稳定类型并执行漂移检查；Vue 使用 `@tiptap/vue-3`，Layui 使用 `@tiptap/core` 建立独立 UI Adapter；服务端通过自有 `IRichTextSanitizer` 抽象和 `HtmlSanitizer` 实现最终安全净化。首期只持久化净化后的 HTML，不把 Tiptap JSON 暴露为公共 API 或数据库永久契约。第一个生产消费者是 Notifications 模块的公告/站内通知，不新增独立的“净化 HTML”公共 Endpoint。

**Tech Stack:** Tiptap Core 3.28.0、Vue 3、TypeScript、原生 ESM/Layui、ASP.NET Core、`HtmlSanitizer@9.0.892`、Vitest、Node Test Runner、xUnit、Playwright。

## Global Constraints

- 只使用 MIT 的 Tiptap Core、Starter Kit 和逐项批准的开源扩展；Tiptap Pro、Cloud、协作、评论、版本历史和 AI 扩展不进入默认框架。
- Art Design Pro 自带的 wangEditor 不随模板迁入；两套编辑器不得并存成为基础依赖。
- 服务端净化是唯一安全边界；客户端过滤只用于预览和即时反馈，任何保存操作都必须再次经过服务端。
- 首期允许段落、标题、有序/无序列表、粗体、斜体、删除线、引用、代码、链接和经 Files API 授权的图片；默认禁止内联样式、脚本、事件属性、`iframe`、`object`、`embed`、SVG、Base64/Data URL 和任意远程媒体。
- 富文本图片、附件和视频必须先通过 Files 模块取得受控资源标识；禁止直接把二进制或外站 URL 写入内容。
- 服务端返回和保存净化后的规范 HTML；业务审计记录修改人、时间和内容摘要，不把完整敏感正文写入普通应用日志。
- Vue/Layui 必须共享同一格式契约和 XSS 样例，但各自实现工具栏、焦点、键盘和 DOM 生命周期；不得让 Vue Runtime 进入 Layui 产物。

---

### Task 1: 冻结富文本契约、依赖与许可边界

**Files:**
- Create: `contracts/rich-text/profile-v1.json`
- Create: `packages/rich-text-contracts/package.json`
- Create: `packages/rich-text-contracts/src/index.ts`
- Create: `packages/rich-text-contracts/src/profile.ts`
- Create: `packages/rich-text-contracts/tests/profile.test.ts`
- Modify: `pnpm-workspace.yaml`
- Modify: `Directory.Packages.props`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: Full.NET 设计令牌、BCP 47 语言治理和 Files 资源契约
- Produces: `RichTextProfileV1`、最大字符/节点限制、允许格式和媒体引用规则

- [ ] **Step 1: 先写失败的契约测试**

断言首期 Profile 具有稳定版本 `1`，禁止 `script`、事件属性、内联样式、Data URL 和未知节点；断言允许格式集合没有依赖 Vue、Layui 或 Tiptap 私有类型，并且 TypeScript 输出与仓库级 JSON Profile 一致。

- [ ] **Step 2: 运行并确认契约尚不存在而失败**

Run: `pnpm --filter @fullnet/rich-text-contracts test`

Expected: FAIL，指出 Profile 或包尚未实现。

- [ ] **Step 3: 实现最小无框架契约并精确锁定依赖**

前端依赖精确锁定 `@tiptap/core@3.28.0`、`@tiptap/vue-3@3.28.0`、`@tiptap/starter-kit@3.28.0` 与实际使用的 MIT 扩展；服务端中央包版本锁定 `HtmlSanitizer` `9.0.892`。许可清单记录 Tiptap 与 HtmlSanitizer 的 MIT 来源，明确 Pro/Cloud 不在发布范围。

- [ ] **Step 4: 运行契约和依赖边界测试**

Run: `pnpm --filter @fullnet/rich-text-contracts test && pnpm audit:clients`

Expected: PASS；包不依赖 UI 框架，锁文件中不存在 wangEditor 或 Tiptap Pro 包。

### Task 2: 建立服务端净化安全边界

**Files:**
- Create: `src/BuildingBlocks/Full.NET.RichText/Full.NET.RichText.csproj`
- Create: `src/BuildingBlocks/Full.NET.RichText/IRichTextSanitizer.cs`
- Create: `src/BuildingBlocks/Full.NET.RichText/RichTextSanitizer.cs`
- Create: `src/BuildingBlocks/Full.NET.RichText/RichTextSanitizationResult.cs`
- Create: `src/BuildingBlocks/Full.NET.RichText/RichTextProfileV1.cs`
- Create: `src/BuildingBlocks/Full.NET.RichText/DependencyInjection.cs`
- Create: `tests/Full.NET.UnitTests/RichText/RichTextSanitizerTests.cs`
- Create: `tests/Full.NET.UnitTests/RichText/XssCorpus.cs`
- Modify: `Full.NET.slnx`

**Interfaces:**
- Consumes: 未受信任 HTML 与 `contracts/rich-text/profile-v1.json` 映射出的 C# `RichTextProfileV1`
- Produces: `RichTextSanitizationResult`，包含规范 HTML、纯文本摘要、是否发生移除和稳定违规代码

- [ ] **Step 1: 使用攻击语料写失败测试**

覆盖脚本标签、大小写/编码混淆、事件属性、`javascript:`/`data:` 协议、CSS URL、SVG、畸形嵌套、标签污染、超长内容和外站图片；同时保留合法的中英文、列表、链接和 Files 资源引用。

- [ ] **Step 2: 运行并确认没有净化实现而失败**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter RichTextSanitizerTests`

Expected: FAIL，指出类型或实现不存在。

- [ ] **Step 3: 实现自有接口和显式白名单**

`RichTextSanitizer` 封装 `Ganss.Xss.HtmlSanitizer`，不使用包的宽松默认集合；显式配置标签、属性和 `http/https` 协议，只保留可解析的规范 Files 资源标记。净化器不得自行访问数据库或当前 HTTP Context；媒体的存在性、租户归属和业务授权由真实消费者在事务边界内验证。

- [ ] **Step 4: 增加幂等、并发和性能门禁**

断言 `Sanitize(Sanitize(input))` 结果稳定，单例配置不可在请求期间修改，并以固定 100 KB 合法/恶意样例记录基准；超过内容上限返回结构化验证错误，不在请求线程反复编译策略。

- [ ] **Step 5: 运行单元测试和依赖漏洞检查**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter RichText && dotnet list Full.NET.slnx package --vulnerable --include-transitive`

Expected: PASS；无已知高危漏洞，所有攻击样例被移除或拒绝。

### Task 3: 实现 Vue Tiptap Adapter

**Files:**
- Create: `ui/admin/src/components/rich-text/FullNetRichTextEditor.vue`
- Create: `ui/admin/src/components/rich-text/createRichTextExtensions.ts`
- Create: `ui/admin/src/components/rich-text/richTextModel.ts`
- Create: `ui/admin/src/components/rich-text/FullNetRichTextEditor.test.ts`
- Modify: `ui/admin/package.json`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Consumes: `RichTextProfileV1`、净化 HTML、Locale、设计令牌和 Files Picker Adapter
- Produces: `modelValue: string` 与无框架的上传/验证事件，不暴露 Tiptap Editor 到业务页面

- [ ] **Step 1: 写失败的 Vue 行为测试**

覆盖双向值、只读、禁用、字符上限、工具栏权限、粘贴清理、链接协议、上传失败、中文/英文文案、键盘操作和销毁后资源释放。

- [ ] **Step 2: 实现最小 Editor Adapter 和允许扩展集**

业务页面只能使用 `FullNetRichTextEditor`；Starter Kit 中未批准的能力必须显式关闭。粘贴 Word/网页内容时删除样式与未知结构，保存前只提交 HTML 字符串。

- [ ] **Step 3: 接入可访问性和按需加载**

工具栏按钮具有可本地化名称、按下状态和焦点顺序；编辑器仅在真实富文本页面异步加载，不进入登录页和普通 CRUD 首屏 chunk。

- [ ] **Step 4: 运行 Vue 测试、类型检查和构建**

Run: `pnpm --filter @fullnet/admin test && pnpm --filter @fullnet/admin typecheck && pnpm --filter @fullnet/admin build`

Expected: PASS；Tiptap 只存在于富文本异步 chunk，业务代码未直接依赖 Editor 实例。

### Task 4: 实现 Layui Tiptap Core/DOM Adapter

**Files:**
- Create: `ui/admin-layui/js/components/rich-text-editor.js`
- Create: `ui/admin-layui/css/components/rich-text-editor.css`
- Create: `ui/admin-layui/tests/rich-text-editor.test.mjs`
- Modify: `ui/admin-layui/js/i18n/resources.js`
- Modify: `ui/admin-layui/package.json`

**Interfaces:**
- Consumes: 与 Vue 完全相同的 `RichTextProfileV1`、HTML 和 Files Picker Adapter
- Produces: `createRichTextEditor(element, options)`，具有 `getHtml`、`setHtml`、`setReadOnly` 和 `destroy`

- [ ] **Step 1: 写与 Vue 镜像的失败测试**

同一组格式、协议、长度、上传、语言和销毁样例必须在 Node DOM 测试中通过；禁止在 Layui 包中出现 Vue Runtime。

- [ ] **Step 2: 使用 `@tiptap/core` 实现独立工具栏和生命周期**

不得复制 Vue 组件 DOM；以 Full.NET CSS Variables 实现 Layui 风格，保留相同命令语义、快捷键和只读行为。

- [ ] **Step 3: 增加 CSP、焦点和资源释放验证**

不使用内联事件处理器或 `eval`；弹层关闭、Tab 切换和页面销毁时注销 Editor、监听器和上传任务。

- [ ] **Step 4: 运行 Layui 测试和生产构建**

Run: `pnpm --filter @fullnet/admin-layui test && pnpm --filter @fullnet/admin-layui build`

Expected: PASS；生产产物不含 Vue/React，CSP 检查无新增例外。

### Task 5: 与 Files 和首个 Notifications 消费者纵向集成

**Files:**
- Modify: `src/Modules/Notifications/`（按模块交付计划确定具体文件）
- Modify: `src/Modules/Files/`（按 Files 模块计划确定具体文件）
- Create: `ui/admin/src/views/notifications/AnnouncementEditorView.vue`
- Create: `ui/admin-layui/pages/notifications/announcement-editor.html`
- Create: `tests/e2e/admin-parity/tests/rich-text-announcement.spec.mjs`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: Notifications 创建/编辑权限、租户上下文、Files 资源 ID 与 `IRichTextSanitizer`
- Produces: 只存储净化 HTML 与稳定 Files 资源标记的公告创建/编辑/预览流程

- [ ] **Step 1: 在 Notifications 模块计划中先定义失败的后端契约测试**

覆盖跨租户图片引用、无权限上传、恶意 HTML、并发修改、内容为空、超长内容和合法格式；禁止建立无业务归属的通用净化 Endpoint。

- [ ] **Step 2: 保存前净化并重新绑定 Files 资源**

事务内解析稳定 Files 资源标记并验证资源租户与用途，保存净化 HTML 和内容摘要；渲染时把资源标记解析为受授权的访问地址，不持久化临时签名 URL。删除/替换图片时按 Files 生命周期规则解除引用，审计日志不记录完整正文。

- [ ] **Step 3: 同步实现 Vue/Layui 公告编辑流程**

两端使用各自 Adapter，支持创建、编辑、预览、取消、权限拒绝、会话失效、服务端净化提示和 TraceId；业务流程和权限码一致。

- [ ] **Step 4: 运行双数据库、双管理端和真实后端 E2E**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --filter RichText && pnpm test:e2e`

Expected: SQL Server/MySQL 均 PASS；持久化内容无攻击载荷，跨租户资源被拒绝，两端预览一致。

### Task 6: 完成安全、兼容和发布验收

**Files:**
- Create: `docs/verification/rich-text-foundation.md`
- Modify: `README.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `THIRD-PARTY-NOTICES`

**Interfaces:**
- Consumes: 前五项的测试、构建、XSS、许可、体积和双库证据
- Produces: 可审计的 `Implemented`/`Verified` 状态和后续升级约束

- [ ] **Step 1: 执行完整富文本攻击回归与浏览器验证**

至少覆盖 Chrome/Edge 的粘贴、撤销重做、键盘、屏幕阅读器标签、移动宽度、CSP 和服务端回读；记录不能自动验证的人工项目。

- [ ] **Step 2: 记录 HTML Profile 兼容策略**

Profile 增加格式时只允许向后兼容；删除或改变现有格式必须提供内容扫描、迁移和回滚方案。编辑器升级必须用历史 HTML 语料进行打开、编辑、保存回归。

- [ ] **Step 3: 执行依赖、许可和产物检查**

Run: `pnpm audit:clients && dotnet list Full.NET.slnx package --vulnerable --include-transitive && git diff --check`

Expected: PASS；MIT 依赖和实际发布资产已登记，不含 Pro/Cloud、wangEditor、Base64 示例资产或未知远程资源。

- [ ] **Step 4: 更新状态且禁止过度声明**

只有服务端净化、Vue/Layui Adapter、首个真实消费者、SQL Server/MySQL 和真实后端 E2E 全部通过后，富文本基础才可标记为 `Verified`；仅完成组件时最多为 `Implemented`。
