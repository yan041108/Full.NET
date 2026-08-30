# Workflow-Vue3 来源与迁移记录

- **状态：** 源码迁移暂缓；授权凭据尚未归档，候选代码含明确禁止能力
- **复核日期：** 2026-08-30
- **官方仓库：** <https://github.com/StavinLi/Workflow-Vue3>
- **上游基线：** `8d81e61edc495d07ae5fdc21e3f24aacc7f32991`
- **本地候选来源：** `G:/wwwroot/github_fork/Admin.NET.Pro.V2.1.AI-master/Web/src/views/dataApproval/flowDefinition/components/workflow-vue3`
- **本地候选快照：** 49 个文件；排序后的 `SHA-256 + 相对路径` 清单摘要为 `D00486497E29B7CFCA686FF28742B8F5E3CE660812A14DB5CCDF7DB14CAF2C83`

## 授权状态

项目所有者已说明取得作者允许，但本次没有读取授权原件，也没有可登记的受控存放位置和校验摘要。该口头/会话事实可以支持架构调研，不能替代源码迁入和再分发所需的可审计凭据。

在授权凭据的受控位置、文档版本/日期、校验摘要和再分发条件被项目所有者或许可负责人登记前，不得把候选源码复制到 Full.NET，也不得更新 `THIRD-PARTY-NOTICES` 声明已采用。

## 上游差异证据

本地候选的 `src` 与固定上游提交的 `src` 进行 `git diff --no-index --numstat` 比较：

| 项目 | 结果 |
| --- | ---: |
| 发生变化的路径 | 32 |
| 新增行 | 7,818 |
| 删除行 | 505 |
| 本地候选源代码行 | 11,331 |

这表明本地版本包含有价值的产品交互积累，但不是可直接更新的轻量上游分支。候选中仍能检出：

- `new Function` 投票脚本；
- `Math.random()` 持久节点标识；
- 任意 `remoteUrl/headers/body` 配置；
- 外部阿里字体和图片 URL；
- Mock/独立入口/全局 CSS 等原项目结构；
- 修改数据、删除数据、动态路由和触发器等与 Full.NET 模块边界不兼容的能力。

因此不能整体复制目录，也不能保留旧数字节点协议或 FlowJson 兼容层。

## 允许迁移的产品资产

授权证据完成归档且 VForm3 路线重新决策后，只允许按文件级审查重写/迁移：

- 递归树形审批画布的布局与交互思路；
- 节点插入、删除、分支、缩放和错误定位；
- 已由 Full.NET 服务端目录标记为 Designable/Publishable/Executable 的节点 Drawer 交互。

Full.NET 目标代码必须使用自己的稳定 NodeKey、强类型 Draft DTO、服务端目录和发布编译器；不得读取或执行旧运行时协议。

## 明确排除

- Mock API、独立 Demo 入口、Pinia 共享可变 Store 和全局样式覆盖；
- LogicFlow 第二套编辑协议、旧数字 NodeType 和旧 API Adapter；
- `new Function`、任意脚本、远程条件/触发器、任意 Header/Body；
- 远程字体、图片与其他未归档资产；
- 直接修改/删除其他模块数据的节点；
- 与 Admin.NET C#/SqlSugar 运行时、表结构和授权模型耦合的代码。

## 更新与迁移流程

1. 先归档授权证据的位置和摘要，并固定新的上游/本地候选快照。
2. 重新生成文件哈希清单和无索引差异，逐文件标记 `rewrite / migrate / exclude`。
3. 先由 Full.NET 服务端目录和强类型 Draft 测试建立边界，再迁移单个交互切片；禁止先复制整个目录后清理。
4. 每个迁移提交必须证明禁用能力没有进入产品源码，且通过 Vue typecheck、Unit、CSP、权限 DOM、production build 和包体门禁。
5. 源码实际进入发布物时，保留授权要求的作者声明并更新 `THIRD-PARTY-NOTICES`。
