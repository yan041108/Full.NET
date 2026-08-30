# VForm3 与 Workflow-Vue3 直接集成实施计划

> 状态：实现完成并验证通过
> 基线：`73004d69746e58d0f2b70561d162d19e676fa3a8`
> 任务快照：`workflow-vform3-direct-20260830`

## 目标

按项目所有者明确决策，将 `vform3-builds@3.0.10` 封装为独立、可复用的管理端表单设计器模块，并将 Admin.NET.Pro 中指定的 `workflow-vue3` 源码复制到 Full.NET 管理端，接入 Workflow 定义设计页。Workflow 只是首个消费方，不拥有 VForm3 宿主。

## 不变量

- 两个第三方设计器只属于 Vue 管理端设计态，不进入 Host.Api Native AOT 可达路径。
- `@fullnet/admin-form-designer` 只提供通用 Host 和生命周期；Workflow Schema 转换留在 Workflow Adapter，禁止通用包依赖 Workflow 契约。
- VForm3 JSON 和 Workflow-Vue3 数字节点树不是公共 API、数据库权威协议或运行时授权依据。
- 保存时必须转换为现有 `WorkflowFormSchema` / `WorkflowDefinitionDraft`；服务端目录、修订号和发布编译器继续失败关闭。
- 不增加 `unsafe-eval`，不放宽 CSP，不允许脚本、HTML、iframe、远程资源或任意请求配置成为可发布能力。
- 页面操作继续使用 `workflow.forms.*` 和 `workflow.definitions.*` 独立权限码。

## 固定来源

- VForm3：`vform3-builds@3.0.10`，精确版本，包归档与许可证信息沿用并更新 `docs/development/third-party/vform3.md`。
- Workflow-Vue3 本地来源：`G:/wwwroot/github_fork/Admin.NET.Pro.V2.1.AI-master/Web/src/views/dataApproval/flowDefinition/components/workflow-vue3`。
- Admin.NET.Pro 来源工作区基线提交：`50b1494d4cced8bdd454de52d1b4511437993e83`；指定目录当时未纳入来源仓库 Git，目录内容以本计划记录的快照摘要固定。
- 指定目录：49 个文件；排序后的 `SHA-256 + 相对路径` 清单摘要为 `73f5ded1909096be2889199b73756de39e76c52ba862685cba8ab204502b05ef`。
- 来源仓库根目录同时提供 MIT 与 Apache-2.0 文本，Web 包声明为 MIT；迁入文件保留来源说明并登记第三方声明。

## 实施步骤

1. 先用单元测试固定两套适配器的双向转换、稳定键、目录过滤和危险配置拒绝行为。
2. 精确安装 VForm3，在独立 workspace 包 `@fullnet/admin-form-designer` 中提供通用延迟加载 Host；Workflow Forms 通过自己的 Adapter Wrapper 使用，业务页面不得访问第三方包内部对象。
3. 复制 Workflow-Vue3 设计器核心源码和本地资产，删除 Demo 入口、Mock HTTP 与未使用工作区文件；外部人员选择、持久化和权限通过 Full.NET Adapter 注入。
4. 当前切片只把 Workflow-Vue3 线性树转换为 Full.NET 的 `start`、`human.approval`、`notify.cc`、`end` 节点；网关等尚未闭合的节点不开放，未进入服务端目录的节点不允许保存或发布。
5. 为定义管理补齐创建、读取、更新 Draft、发布和节点目录的前端调用，再把设计器接入现有页面权限边界。
6. 执行类型检查、单元测试、生产构建、治理/CSP 静态检查和任务快照影响集验证；分别约束首屏静态图与 VForm3 延迟块，记录真实结果后提交。

## 回退

VForm3 和 Workflow-Vue3 均通过内部入口组件接入。若 Workflow 上线验证失败，可把页面入口切回现有原生 `WorkflowFormDesigner`，无需删除通用表单设计器包，也无需修改服务端 Schema、数据库或已发布版本。
