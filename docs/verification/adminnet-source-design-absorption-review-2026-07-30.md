# Admin.NET.Pro 源码设计吸收复核

- 日期：2026-07-30
- 状态：已完成静态源码复核；结论用于改造排序，不证明任何待开发能力已实现
- Full.NET 基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- Admin.NET.Pro 基线：`v2.1@3879b035791b4603e734c15e7c316e0aeca32f1b`
- Admin.NET.Pro 基线提交日期：2026-07-13
- 范围：`Admin.NET.Core`、内置服务、代码生成、认证、租户、组织、角色、文件、作业、日志、事件总线，以及 10 个 `Admin.NET.Plugin.*`
- 方法：静态代码结构盘点、关键调用链阅读、Full.NET 当前实现交叉检查、功能矩阵和架构规格一致性复核
- 未执行：Admin.NET.Pro 运行时、数据库迁移、前端交互、压力测试和漏洞扫描

## 1. 总体结论

Admin.NET.Pro 最值得 Full.NET 吸收的不是 Furion、SqlSugar 或实体继承本身，而是已经产品化的后台能力目录、管理流程和扩展点。源码复核新增确认了下列高价值设计：

1. 代码生成按单表、树表、主从表和关系表选择场景策略，并支持预览、菜单和字段组件配置；
2. 角色数据范围、字段展示授权、用户列个性化分别建模，而不是只依赖菜单；
3. 作业定义、触发器、执行记录和集群节点分别持久化；
4. 文件元数据与本地、数据库、OSS、SSH 等存储实现解耦；
5. 流水号支持租户/全局作用域、格式插槽、预览和重置周期；
6. 公告发布后生成接收人记录，并维护个人已读状态；
7. 访问、操作、异常、HTTP、差异、事件和开放接口日志按用途拆分；
8. 文档、工作流、审批、AI 和第三方平台插件都包含可复用的业务子能力，而不只是 CRUD。

这些设计应按 Full.NET 的模块、Dapper、双数据库、标准 HTTP、Outbox、可信租户上下文和双管理端边界重新实现。Admin.NET.Pro 中依赖全局过滤、动态编译、运行时建库、任意过滤逃生口或缓存锁的实现不能原样进入 Full.NET。

## 2. 吸收分级

| 级别 | 含义 | 处理原则 |
| --- | --- | --- |
| A：优先吸收 | 产品价值明确，能在现有 Full.NET 边界内形成独立纵向切片 | 纳入近期计划，测试先行并完成双库/双端验证 |
| B：重构吸收 | 语义有价值，但原实现与安全、租户、事务或模块边界冲突 | 先建立独立规格或安全决策，再实现 |
| C：兼容隔离 | 旧 Admin.NET 客户端或迁移项目可能需要，但不应成为核心默认能力 | 只进入 Compatibility、Provider 或受控工具 |
| D：拒绝原实现 | 原机制会削弱 Full.NET 已建立的不变量 | 不复制；只保留等价业务目标或明确标记不适用 |

## 3. Core 源码观察与 Full.NET 决策

### 3.1 实体接口与基类的逐项结论

| Admin.NET.Pro 类型 | 参考价值 | Full.NET 落点 |
| --- | --- | --- |
| `ITenantIdFilter` | 高：把“该实体受租户隔离”表达为显式能力 | 进入代码生成 `EntityCapabilities` 和生成期校验；运行时继续使用可信租户上下文、`SqlDataScope.TenantRequired`、`SqlTenantBinding.CurrentTenantId` 与显式 SQL，不复制可空 `long TenantId` 或全局过滤器 |
| `IDeletedFilter` | 高：统一软删除语义，并提醒所有读写路径处理删除状态 | 进入 `DeleteMode`；生成查询、更新、删除和唯一性策略时显式处理 `IsDeleted`，不得依赖 ORM 全局过滤，也不得让超级管理员绕过删除状态 |
| `IOrgIdFilter` | 中高：说明数据归属与机构范围需要被建模 | 进入资源级 `OwnershipMode` 和 Organization DataScope 投影；不把所有业务所有权都等同为 `CreateOrgId`，也不接受客户端机构 ID 作为授权依据 |
| `EntityBaseId` | 仅有“统一主键”概念价值 | 不复制雪花 `long`；Full.NET 官方实体继续使用应用端生成的 UUID v7 `Guid` |
| `EntityBase` | 高：创建、更新、删除审计字段是一组可复用能力 | 吸收为可组合的生成元数据与模块内记录契约，不建立跨模块万能继承树，避免强迫所有表携带无意义字段 |
| `EntityBaseData` | 中高：把创建机构纳入数据范围意识 | 拆成显式审计能力与领域所有权能力；只有真实由组织拥有的资源才生成组织所有权字段和范围 SQL |

结论是“参考能力模型，不复制继承模型”。这些类型最有价值的部分是让租户、软删除、审计和组织归属可被工具识别；最不适合 Full.NET 的部分是雪花 `long`、可空租户、SqlSugar 全局过滤和一个基类覆盖所有领域。计划 Task 1、2 正是对这组语义的洁净室改造。

### 3.2 其他 Core 能力

| Admin.NET.Pro 设计 | 源码证据 | 价值与风险 | Full.NET 决策 |
| --- | --- | --- | --- |
| 实体能力接口和基类 | `Admin.NET.Core/Entity/IEntityFilter.cs`、`EntityBase.cs` | 清楚表达租户、软删除、审计、创建机构等能力；但绑定雪花 `long`、SqlSugar、导航属性和可空租户 | A：吸收为代码生成 `EntityCapabilities`，不建立万能运行时实体基类 |
| SqlSugar 全局过滤 | `SqlSugar/SqlSugarSetup.cs:321-337` | 自动过滤方便；超级管理员提前返回会同时跳过删除、租户和机构过滤，且 `ClearFilter`/`IgnoreTenant` 形成广泛逃生口 | D：继续使用 `SqlDataScope`、`SqlTenantBinding`、显式 SQL 和架构测试 |
| 创建者/机构数据范围 | `SqlSugar/SqlSugarFilter.cs`、`Service/Org/SysOrgService.cs` | 支持本人、机构、下级和自定义范围；但把通用业务归属简化为创建人/创建机构 | B：保留规范化 DataScope，按模块资源投影到显式参数化 SQL |
| 多机构和主机构 | `SysUserExtOrg`、`SysUserService`、`SysOrgService` | 用户可有主机构与扩展机构，数据范围取多个来源并集 | A：继续扩展 Organization 用户隶属模型和角色范围并集 |
| 角色复制和字段授权 | `Service/Role/SysRoleService.cs`、`SysRoleTableService.cs` | 角色复制覆盖菜单、机构、接口和字段；字段权限提升后台交付能力 | B：字段权限必须使用稳定资源/字段键，禁止暴露物理表列名，且不能只靠前端隐藏 |
| 租户初始化和独立库 | `Service/Tenant/SysTenantService.cs:135-578` | 一次完成机构、管理员、角色、职位和菜单；支持共享库/独立库 | B：吸收“租户开通工作流”和独立库连接选择；迁移与 Baseline Seed 仍只能由 Migrator 执行 |
| 登录方式目录 | `Service/Auth`、`OAuth`、LDAP、API Key、Signature Auth | 密码、LDAP、OAuth、API Key、签名认证形成完整企业入口 | A/B：API Key 已有；请求签名和外部身份按独立 Provider/协议规格实现 |
| Token 版本和单用户登录 | `SysAuthService.CreateToken`、`SysOnlineUserService` | Token 版本、在线会话和强制下线有产品价值 | A：Full.NET 已有刷新会话与强制下线，继续以服务端会话版本为事实源 |
| 请求签名 | `SignatureAuth/SignatureAuthenticationHandler.cs` | 时间戳、nonce、HMAC、认证事件和失败审计值得吸收 | B：签名必须覆盖规范化 method/path/query/body digest，并用原子 nonce 存储防重放；日志禁止记录签名原文和请求体敏感数据 |
| 防重复请求 | `Attribute/IdempotentAttribute.cs` | 提供短窗口重复提交抑制 | B：只能命名为重复提交抑制；真正业务幂等必须使用调用方幂等键、结果重放和数据库唯一约束 |
| 数据脱敏 | `Attribute/DataMaskAttribute.cs`、`Extension/ObjectExtension.cs` | DTO 字段按规则脱敏有复用价值 | B：脱敏是输出防护，不代替字段授权；规则应绑定稳定字段键并覆盖日志、导出和 AI 上下文 |
| 代码生成场景策略 | `CodeGen/CodeGenStrategyFactory.cs`、`CodeGen/Strategies/*` | 单表、树表、主从表、关系表策略，预览和字段组件配置成熟 | A：扩展 `FullNetCrudSchema` 为显式场景/关系/能力模型，保持确定性生成和安全 Apply |
| 代码生成直接写盘 | `Service/CodeGen/SysCodeGenService.cs:543-590` | 预览、菜单生成有价值；递归删除旧目录并直接覆盖写盘不可接受 | D：继续使用 Manifest 所有权、原子提交、冲突零写入和 committed tombstone |
| 列显示个性化 | `Service/ColumnCustom/SysColumnCustomService.cs` | 固定、宽度、顺序、可见性按用户和 Grid 保存，产品价值高 | A：进入 Settings/Client Preferences；服务端只接受客户端登记的稳定 Grid/Column 键 |
| 流水号规则 | `Service/Serial/SysSerialService.cs`、`ISerialSlotProvider.cs` | 租户/全局序列、格式插槽、周期重置、预览完整 | A：建立 SerialNumbers 模块；数据库原子分配，明确唯一但允许间隙，不依赖缓存锁保证正确性 |
| 作业持久化 | `Service/Job/DbJobPersistence.cs`、`JobMonitor.cs` | 定义、触发器、执行历史、集群状态和手动控制值得吸收 | A：扩展现有 Jobs 的 cron/一次性触发、误触发策略、执行详情和运行控制 |
| 动态脚本/HTTP 作业 | `DynamicJobCompiler.cs`、`HttpJob.cs` | 灵活但等价远程代码执行或 SSRF | C/D：动态 C# 作业拒绝进入默认发布；HTTP 作业只能作为带目标白名单和密钥引用的 Provider |
| 文件 Provider | `Service/File/FileProvider/ICustomFileProvider.cs` 及实现 | 元数据与存储解耦，覆盖本地、DB、OSS、SSH | A：从现有本地 Blob 边界演进为流式 Provider；首个外部 Provider 出现时再建立独立 Contracts 程序集 |
| 文件去重和预览 | `Service/File/SysFileService.cs` | 内容摘要、重复检测、目录、预览和业务关联有价值 | B：使用流式 SHA-256、租户/授权边界和明确引用模型；禁止全量缓冲与 MD5 安全语义 |
| 通知接收和已读 | `Service/Notice/SysNoticeService.cs`、`SysNoticeUser` | 公告、指定接收人、个人已读和在线推送形成闭环 | A：Full.NET Inbox 已吸收主要语义，后续补公告受众快照、撤回和投递审计 |
| 列表筛选协议 | `Utils/System/BaseFilter.cs` | 统一 Search/Filter 对快速后台有价值 | B：仅允许资源声明的字段和运算符，排序/过滤片段来自封闭白名单，不解析任意物理列 |
| 审计日志分类 | `Logging/*`、`Service/Log/*` | 访问、操作、异常、HTTP、差异、事件、开放接口分层清晰 | A/B：吸收分类和查询体验；默认不持久化完整请求/响应、Token、连接串或异常对象 |
| 事件重试与日志 | `EventBus/RetryEventHandlerExecutor.cs` | 处理器重试记录、耗时和失败分类有可观测价值 | B：吸收遥测和错误分类；可靠业务事件仍必须通过事务 Outbox，不能用内存事件重试替代 |
| 缓存管理和前缀失效 | `Cache`、`SysCacheService`、多处 `RemoveByPrefixKey` | 后台可观测和人工失效有价值；前缀扫描和逐节点手工失效容易漂移 | B：使用 FusionCache 标签/版本化键和提交后事件失效；管理端只暴露受控目录与精确操作 |
| 动态插件编译 | `Service/Plugin/SysPluginService.cs:101-139` | 插件目录和启停体验有价值；数据库 C# 编译为动态 WebAPI 是高危 RCE | D：拒绝运行时任意 C#；改为签名包、静态清单、启动期发现、版本/依赖/许可检查 |
| APIJSON、在线 DDL 和超级 API | `Service/APIJSON`、`DatabaseTools`、`Plugin.ReZero` | 对旧项目和低代码场景有需求；默认开放会扩大 SQL 注入、越权和破坏面 | C：仅进入 Compatibility/受控工具，使用预定义 Schema、审批、Dry Run、备份和审计 |

## 4. 插件源码观察

| 插件 | 值得吸收的业务模型 | 需要重构的边界 |
| --- | --- | --- |
| AI | 模型配置、会话历史、摘要、SSE、模型切换、调用日志 | 使用 `Microsoft.Extensions.AI`；Tool 显式授权、额度、人工审批、MCP/AG-UI 协议隔离 |
| DataApproval | 审批配置、表单记录、变更前后快照、流程绑定 | 禁止以通用中间件拦截任意写请求；审批必须由用例显式声明并与业务事务/Outbox 协作 |
| Document | 分类、标签、版本、权限、分享、预览、回收站、统计和日志 | 文件内容由 Files Provider 管理；权限、分享口令、病毒扫描和版本回滚必须独立建模 |
| WorkFlow | 定义、表单、流程、实例、步骤、待办、抄送、日志、暂停/恢复/撤销/驳回 | 流程定义版本不可变；执行器需租约、幂等、补偿、Outbox 和可恢复 Worker，不能依赖动态类型扫描 |
| DingTalk/WorkWeixin | 组织同步、用户同步、消息、卡片、审批和 Token 管理 | 作为 Provider；凭据进入 Secret Store，Webhook 验签、限流、重试、幂等和审计必需 |
| GoView | 项目、数据、登录兼容和 OSS 地址适配 | 客户端和 API 适配独立，不让 GoView 包络进入标准核心 API |
| K3Cloud | 登录、接口契约和推送结果 | Provider + Sample；外部调用使用 Outbox/Inbox、幂等键和业务对账 |
| PaddleOCR | OCR Provider 接口 | 文件大小/类型、资源限额、超时、结果置信度和敏感数据治理 |
| ReZero | 动态表、动态接口和授权体验 | 只吸收受控 Schema/审批/预览；拒绝任意动态 API 和无边界数据库修改 |

## 5. 与 Full.NET 当前实现的差距

| 能力 | 当前状态 | 源码复核后的增量缺口 |
| --- | --- | --- |
| 代码生成 | 已有确定性 CRUD、双库迁移草案、双端模型、安全 Apply | 缺实体生命周期/审计/所有权能力、树表/主从/关系场景、字段组件语义和生成任务记录 |
| Identity/Organization | 已有角色、权限、数据范围、用户机构/职位 | 缺稳定字段投影授权、外部身份 Provider、请求签名和更完整角色复制 |
| Settings | 已有配置、字典、枚举目录；用户 Grid/Column 偏好已完成 Build-verified 服务与双端适配器 | 具体 Grid 的可视化编辑与真实浏览器 E2E 按消费者继续接入 |
| Jobs | 已有定义、手动触发、执行记录、Worker、重试和租约 | 缺 cron/一次性触发、时区、误触发、暂停/恢复和更完整执行历史 |
| Files | 已有 Host 元数据、本地流式存储、软删除与 Worker 清理 | 缺正式外部 Provider 边界、对象存储实现、内容扫描、引用关系和租户文件 |
| Notifications | 已有公告、个人收件箱、未读/已读和实时修复 | 缺受众快照、撤回、多渠道投递状态和用户通知偏好 |
| Auditing | 已有访问、操作、异常、批量写和保留 | 缺出站 HTTP/开放接口/事件执行审计目录，以及字段级脱敏策略 |
| SerialNumbers | **Build-verified**：Host 规则 API、纯预览、Host/租户作用域、UTC 重置、幂等原子分配与双库 039 已交付 | 双管理端规则页和真实栈 E2E 尚未交付，不能标记 Verified |
| Modularity Admin | 仅有静态模块目录与 Composition | 缺可查询清单、版本/依赖/许可/健康状态；动态代码执行明确禁止 |

## 6. 推荐实施顺序

1. **P0 生成基础**：实体能力配置、软删除/审计/并发生成不变量、场景策略目录；
2. **P1 快速交付**：用户列个性化和 SerialNumbers；
3. **P1 运行能力**：Jobs 触发器、Files Provider、通知受众/投递状态；
4. **P1/P2 安全能力**：稳定字段投影授权、请求签名、外部身份；
5. **P2 可观测和模块治理**：出站 HTTP/开放接口/事件审计、只读模块清单；
6. **M5+ 独立模块**：Document、Workflow、DataApproval、ImportExport、Reporting、AI 和第三方 Provider；
7. **Compatibility**：APIJSON、在线 DDL 和 ReZero 类能力最后评估，默认不进入核心。

详细任务、文件和验证门禁见[Admin.NET 设计吸收改造实施计划](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md)。

## 7. 授权与来源

本次只提取能力、交互和架构语义，没有把 Admin.NET.Pro 源码复制到 Full.NET。即使具体源码文件声明 MIT/Apache，Admin.NET.Pro 整体的二开/商用授权也不能自动证明 Full.NET 可将所有代码和资产按 MIT 再许可。任何直接复用必须逐文件登记来源、许可证文本、修改和再分发依据；无法证明时继续采用洁净室独立实现。

## 8. 未验证项

- 未运行 Admin.NET.Pro，因此没有验证动态过滤、租户独立库、作业集群和插件在真实部署中的正确性；
- 未核对 Admin.NET.Pro Web、Web_Artd、App 和 Web_Desktop 的全部交互细节；
- 未对 Admin.NET.Pro 依赖执行安全公告、许可证传递和发布物扫描；
- 插件表只完成源码结构和关键服务抽样，不构成每个插件的完整规格；
- 本记录不改变 `Mapped`、`Implementing`、`Implemented` 或 `Verified` 状态。
