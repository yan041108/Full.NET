# VForm 3 完整源码来源与适配记录

- 来源：<https://github.com/vform666/variant-form3-vite>，固定提交 `c67479e496bab56a93a3dff168a4f529d8293c67`，版本 3.0.10。
- 许可：`Variant Form 许可条款 1.0`，不是 MIT；完整上游文件及作者声明保留在 `upstream/`，原始许可在 `upstream/license.txt`。编译产物附带作者声明、许可及依赖 Notices。
- 迁入：2026-10-24；338 个来源文件，逐字节 SHA-256 清单为 [manifest.json](manifest.json)，由 `tests/source-inventory.test.mjs` 验证。原上游文件未直接修改。
- 范围：3 个原版可添加容器和 card 扩展、18 个基础字段、4 个高级字段及 alert 扩展，共 27 个添加入口；另保留 grid-col/table-cell/tab-pane 等内部节点。属性编辑器、原版 Renderer、工具栏、语言、图标、样式、代码生成器及扩展注册均保留。
- 官方示例外部资源按原 `templatesConfig.js` URL 固定下载到 `runtime-public/templates/`，16 个文件的 URL、日期、字节和摘要另记 [external-assets.json](external-assets.json)，不混算进上游 Git 提交文件清单。官方主从表示例含此社区提交未提供的 sub-form，不能据此宣称社区版新增了该组件。

## 必要适配

适配代码位于 `runtime/`、`src/VForm3DesignerHost.vue`、`src/frame-client.ts` 和 `vite.runtime.config.mjs`，不覆盖原始来源：

1. 旧 `import.meta.globEager` 转为当前 Vite eager glob；保留完整注册集合。原版 VueDraggable CommonJS 包使用当前 Vue 与 SortableJS，避免旧 UMD 全局运行时。
2. Ace worker/HTML mode、图标、模板及格式化依赖本地化；保留全部工具栏，按各开发端口隔离产物。
3. Quill 2 保留上游值与事件协议；HTML 导入、变更和 semantic HTML 导出由 DOMPurify 清理。低风险公告 [GHSA-v3m3-f69x-jf25](https://github.com/advisories/GHSA-v3m3-f69x-jf25) 的精确路径、缓解和限期见根目录安全策略，不能把它表述为上游已修复。
4. 原版开关初始化可能早于父表单引用，字段校验延后到 nextTick；HTML 展示字段生成 SFC 时补齐字符串表达式与属性转义，Vue 3 导出结果纳入编译验证。
5. 上游存储备份改用帧内内存 localStorage；保留原版历史操作，实际保存通过受保护 API/数据库完成。导入切换新实例并从空画布初始化，避免旧备份和退场控件影响新选择；撤销栈保留空画布起点，根扩展在宿主读写及 JSON 导出时保留。
6. 脚本、CSS、HTML、事件及编辑器完整存在于无同源权限的 iframe。父文档限制 frame-src，帧内只允许专用静态/演示路径；固定消息协议校验来源、窗口、通道及请求，不传递管理端凭据。Axios 不读取沙箱无权访问的 XSRF Cookie。
7. 集成样式使空画布提示忽略指针事件，补足拖放区高度，退场节点禁用指针事件；原版 Sortable 真实拖入、跨列移动和合并回显已验证，未修改原始来源样式。
8. 开关默认值属性编辑器显示 null 的布尔投影，用户操作时才回写，防止打开属性就改成 false。开发和交付使用独立生产构建，完整产物按 `contracts/workflow/vform3-runtime-release.json` 实际摘要固定；宿主 Ready、发布和办理均检查当前产物身份。原版 338 文件摘要保持不变。

旧 ESM 精简面板与 VForm→简化 Schema→回显适配已移除。Workflow 独立保存完整 JSON，业务投影只在发布时编译。原版功能和设计资产完整保留，不代表所有生产业务输入、Files 连接器或跨端运行已完成。
