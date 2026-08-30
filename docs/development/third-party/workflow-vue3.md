# Workflow-Vue3 来源与迁移记录

- **状态：** 已按项目所有者 2026-08-30 明确决策迁入设计器核心
- **官方仓库：** <https://github.com/StavinLi/Workflow-Vue3>
- **上游基线：** `8d81e61edc495d07ae5fdc21e3f24aacc7f32991`
- **指定本地来源：** `G:/wwwroot/github_fork/Admin.NET.Pro.V2.1.AI-master/Web/src/views/dataApproval/flowDefinition/components/workflow-vue3`
- **来源工作区基线提交：** `50b1494d4cced8bdd454de52d1b4511437993e83`（指定目录在该来源工作区中尚未纳入 Git，因此此提交只用于固定其余仓库状态，不能单独复现目录内容）
- **来源目录快照：** 49 个文件；排序后的 `SHA-256 + 相对路径` 清单摘要 `73f5ded1909096be2889199b73756de39e76c52ba862685cba8ab204502b05ef`
- **来源仓库许可：** 根目录提供 MIT 与 Apache-2.0 文本，Web 包声明为 MIT
- **目标目录：** `ui/admin/src/workflow/vendor/workflow-vue3`

## 迁入范围

本次复制递归节点画布、节点插入、错误对话框、Pinia 设计态 Store、本地样式和图片资产。独立 Demo 入口、Router、Mock HTTP、Axios、API、Setting 页面和工作区文件未迁入，因为它们不是 Full.NET 嵌入式组件的一部分。

迁入后进行了 Full.NET 定向收口：

- 节点键改为 `crypto.randomUUID()`，不再用 `Math.random()` 生成持久标识。
- 移除外部阿里字体和图片 URL。
- 移除 `new Function` 投票校验，脚本型投票节点直接失败关闭。
- 节点添加菜单只暴露当前适配器支持的审批人与抄送人。
- 远程条件、触发器、修改数据、删除数据、动态路由和其他未进入服务端目录的节点不能保存。
- 业务页面不使用候选 Mock/API；持久化只通过 Full.NET Workflow Definition API。

## 协议边界

Workflow-Vue3 的数字节点树只存在于 Vue 设计态。`workflow-vue3-adapter.ts` 负责与 `WorkflowDefinitionDraft` 双向转换，稳定 NodeKey、节点目录、修订冲突、租户隔离、权限和发布编译继续由 Full.NET 权威边界控制。

当前产品适配器开放线性 `start -> human.approval / notify.cc -> end`。来源画布包含的其他节点 UI 代码不构成服务端能力；后续只有在 `WorkflowNodeTypeCatalog`、编译器、运行时和双库测试一起闭合后才能逐项开放。

## 更新流程

升级时必须优先取得可追溯的来源提交；若来源目录仍未纳入其仓库，则必须重新生成并复核完整目录摘要，不能把工作区基线提交当作目录来源证明。随后逐文件比较本地硬化差异，并执行 Vue typecheck、单元测试、权限 DOM、严格 CSP、production build、包体和服务端发布编译验证。不得用上游数字协议替换 Full.NET 的公共契约。
