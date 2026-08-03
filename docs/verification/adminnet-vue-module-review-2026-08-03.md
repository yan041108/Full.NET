# Cursor Admin.NET 模块吸收审查记录

- 日期：2026-08-03
- 审查基线：`8f520bc1..469a0836`
- 任务快照：`cursor-adminnet-module-review-20260803`
- 范围：授权目录、Files、Notifications、Jobs、CodeGeneration、SerialNumbers、Document 及 Vue 管理端
- 结论：**Build-verified；存在已修复缺陷与后续产品差距，不得整体标记为 Verified**

## 1. Admin.NET 参考边界

本次仅参考本地 `G:/wwwroot/github_fork/Admin.NET.Pro` 的产品能力、页面信息架构和实体语义，不复制其源码或资产。重点参考：

- `Admin.NET/Admin.NET.Core/Entity/SysSerial.cs`：规则格式、重置周期、上下界、排序和状态；
- `Admin.NET/Admin.NET.Core/Entity/SysNotice.cs`、`SysNoticeUser.cs`：公告类型、发布/撤回状态、受众和已读状态；
- `Admin.NET/Admin.NET.Core/Entity/SysFile.cs`：文件元数据目录；
- Jobs 的 `SysJobDetail`、`SysJobTrigger`、`SysJobTriggerRecord`、`SysJobCluster`：定义、触发、执行历史和集群观察；
- Document 插件的 Document、Category、Tag、Version、Share、Permission、Log：文档库完整产品边界；
- Vue 页面 `system/serial`、`system/notice`、`system/file`、`system/job` 及 Document 插件页面：列表、筛选、树、抽屉、确认和状态反馈交互。

Full.NET 保留自己的 UUID v7、Dapper 显式 SQL、双库、标准 HTTP、精确 Endpoint 权限和模块契约边界。明确不吸收 Admin.NET 的动态程序集/类型执行、脚本任务、公共文件 URL、物理存储路径或超级管理员绕过。

## 2. 发现与修复

| 等级 | 发现 | 处理 |
| --- | --- | --- |
| Important | 只有 `jobs.schedules.read` 的角色进入计划页时，页面仍请求需要 `jobs.definitions.read` 的定义目录，403 会使整个计划页失败 | 已改为计划列表独立加载；无定义读取权限时不请求定义 API；创建计划必须同时具备所需定义目录权限，编辑不被误阻断 |
| Important | Document 下载按钮只按 `document.host_documents.read` 显示，并使用 `window.open`，不会携带 Bearer Token | 已按 `files.files.download` 隐藏按钮，并改用认证 Blob 请求；后续 P0 将建立 Document 自有下载 Endpoint，移除对宽泛 Files 权限的产品依赖 |
| Important | Document“添加版本”内部调用 Files 上传，但未检查 `files.files.upload` | 已失败关闭；缺少 Files 上传权限时不创建入口。后续 P0 改为 Document 自有上传版本边界 |
| Important | Jobs 计划、流水号和代码生成模板列表请求失败时只保存 ProblemDetails，页面没有任何可见错误 | 已统一渲染稳定错误码、标题和 traceId |
| Normal | 代码生成模板删除没有二次确认 | 已补 Element Plus 警告确认和取消分支 |
| Important | Cursor 交付的 Vue 生产构建存在 6 个 TypeScript 错误：授权树 `unknown` 未缩窄、回滚确认参数联合类型不兼容、Document 夹具字段漂移和动态 i18n 键未收窄 | 已逐项修复并以独立 `vue-tsc` 和生产构建验证；未关闭类型检查 |
| Important | W4–W5 affected merge 在 185/270 中断，但文档把能力标记为 `Verified` | 已降为 `Build-verified`；完整 merge 通过前不得提升 |
| Normal | 收口文档称“模块/页面/操作三级授权未做”，实际 API 和 Vue 已交付 | 已纠正文档，三级授权树列为已完成 |

回归测试覆盖上述 UI 行为：`HostJobSchedulesView.test.ts`、`HostDocumentItemsView.test.ts`、`SerialNumberRulesView.test.ts`、`CodeGenerationTemplatesView.test.ts`。

## 3. 表模型和产品差距

| 模块 | 已有合理设计 | 下一步应吸收 | 不应照搬 |
| --- | --- | --- | --- |
| SerialNumbers | 规则与计数状态分表、幂等键、作用域、UTC 重置、Pattern/Min/Max/排序/版本 | 分页筛选、规则帮助、有效时间输入、动态预览日期和业务化校验 | 把当前序号混入规则行；依赖展示文本判断重置语义 |
| Jobs | allowlist Handler、Cron/时区规范化、误触发策略、租约/重试/历史 | 自包含定义选项、Cron 解释器、时区选择、下一/上次执行、执行历史、只读 Worker/集群健康 | 动态 `AssemblyName`、任意 CLR 类型、在线脚本代码 |
| Notifications | 公告状态、Outbox/Realtime、个人收件箱已读状态 | 公告类型、受众、发布/撤回审计、分页筛选；收件人选择器 | 在公告行冗余不可校验的组织/用户名称；按翻译文本驱动状态 |
| Files | ProviderKey、StorageKey、实际大小、哈希、Pending/Publishing/Ready 状态机和补偿优于参考 | 分页、类型/Provider/状态筛选、安全预览策略 | 公共 URL、物理 FilePath、向页面暴露 StorageKey |
| Document | 分类、标签、条目、版本基础表及软删除/审计/乐观并发 | 文档自有内容权限、分类树、标签关联、版本历史、持久化回收站；之后再做分享、ACL、预览、日志、统计 | 直接依赖 Files 实现项目；把文件字节或公开路径写入 Document 表 |
| CodeGeneration | 严格 Schema、摘要、审计、乐观并发和受控 Apply/Rollback | 分页筛选、Schema 表单编辑/校验、差异预览和版本历史 | 浏览器任意执行源码、未经审核直接写工作区 |

## 4. 验证状态

审查开始时的现有门禁为：client-contracts 124/124、Vue 368/368、OpenAPI 73/73、Governance 17/17、Naming 24/24、SQL safety 5/5、Integration tooling 32/32、Unit 1100/1100、Architecture 78/78。审查新增回归测试后必须以本次最终新鲜输出为准；永久测试门槛只维护在 `eng/testing/test-matrix.json`。

本轮修复后的新鲜结果：

| 命令 | 结果 |
| --- | --- |
| `pnpm --filter @fullnet/admin test` | 112 files / 377 tests 通过 |
| `pnpm --filter @fullnet/admin build` | `vue-tsc` 与 Vite production build 通过 |
| `pnpm --filter @fullnet/client-contracts test` | 47 files / 124 tests 通过 |
| `pnpm test:openapi` | 73/73 通过 |
| `pnpm test:governance` | 17/17 通过 |
| `pnpm test:naming` | 24/24 通过 |
| `pnpm test:sql-safety` | 5/5 通过 |
| `pnpm test:integration:tooling` | 32/32 通过 |
| `pnpm test:skills` | 两个项目 Skill 契约通过 |
| 当前任务 affected merge plan | `none`（本轮差异只有客户端、contracts TypeScript 与文档） |
| W4–W5 program affected merge plan | 167 files、预计约 46 分钟；此前执行中断，本轮未伪称通过 |

独立权限/模块复审两轮均为 Critical `0`；复审发现的确认重入、重复权限码、Jobs options 授权歧义和 Files 上传契约缺口均已修正。

完整后续执行顺序见[Admin.NET Vue 模块对标下一波计划](../superpowers/plans/2026-08-03-adminnet-vue-module-parity-next-wave.md)。Layui 保持冻结。
