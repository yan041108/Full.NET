# Admin.NET.Pro 逐模块功能差异与执行候选清单（2026-09-05）

## 1. 基线、口径与结论

- Full.NET：`a00475a82c8d268771d20f91e6162c9e73b12d24`，分支 `codex/migration-114-118-recovery-tests`。
- 参考：本地授权仓库 `G:/wwwroot/github_fork/Admin.NET.Pro`，`v2.1`，`fd99e2b58aa1a834d10a87abcf57287ea65a5905`（2026-09-04）。
- 参考范围：README 的 26 类功能、Core/Service 服务目录、10 个业务插件目录、Web 的 system/workFlow 页面、Full.NET 模块 Endpoint/Domain/Provider 与 Vue 页面。
- 相对旧对标基线 `09d38bd8`，参考仓库新增 26 个提交。本报告按本地固定提交对标，不声称覆盖网上其他版本或未提供的商业插件。
- 方法：服务方法/页面入口清点，关键实现阅读，Full.NET 路由、契约、注册和页面交叉确认。没有启动两套系统，没有执行浏览器或数据库测试。
- 本报告是源码复核及执行建议，补充并纠正旧路线图的过时结论；不修改已批准架构、不将源码存在升级成 `Verified`，也不自动批准新模块的 Spec。
- 使用 `fullnet-module-delivery` 的能力映射与模块所有权约束组织清单。

结论：Full.NET 已具备较广的后台基础功能，Workflow/Notifications 近期进展尤其明显；主要欠账是现有模块中的具体用户操作、DataApproval 业务闭环，以及 ImportExport/Reporting/Printing、运维控制面、外部生态模块。不能把整个已实现模块重新立项，也不能把一个首切片等同于整个模块功能对齐。

状态口径：

| 标识 | 含义 |
| --- | --- |
| 已有 | 当前源码有对应实现；仍需按最终验收批次核对运行结果 |
| 部分 | 同一模块已有实现，但本行具体能力存在缺口 |
| 缺失 | 本次扫描范围内没有找到对应产品 API/页面或生产适配器 |
| 差异 | 当前架构有意采用其他方式；不是默认补做原实现 |
| 待核 | 有组件、文档或示例证据，但不足以确认完整功能，先核验再排实现 |

“已有”不代表完全对标；表中“差异”也不是已批准的 `Not Applicable`。执行编号均指本文后面的新一轮候选清单，与此前已完成的 14 个任务无关。

## 2. 已完成能力去重与旧文档纠偏

以下能力不再列为“从零开发”：

1. 用户服务端资料规范化/格式校验/唯一性、固定 Excel 模板与文件导入导出。
2. 日志文件清单、有界尾读、下载和 Vue 控制面。
3. Workflow 指定用户/角色成员/机构负责人/发起人/发起人主部门负责人解析。
4. 多人审批 all/any/nOfM、前后加签、退回、排他/并行/包容网关。
5. 实例暂停/恢复、待办改派、超时策略与恢复 Worker。
6. Workflow → Notifications 的可靠通知投影与内建模板。
7. 邮箱端点验证码发送/验证、模板语言变体、回执详情展示。
8. DataApproval 的首个流水号规则更新审批场景。
9. 114–118 专用双 Provider 恢复测试已经在当前分支新增；仅编译/发现和静态门禁有本地结果，真实数据库执行待 CI。

旧的 `adminnet-feature-parity.md` / `capability-status.md` 仍有“DataApproval Mapped”“邮箱验证未实现”等文字，且部分 9 月 5 日验证记录中的 OpenAPI/跨模块引用待办已被后续审查修复。排期时以当前代码复核为准，历史记录保留当时事实，不能原样复制成新任务。

另有两点必须避免混淆：

- Admin.NET `SysScheduleService` 是用户日程，Full.NET Jobs 是后台调度，两者不能互相抵消。
- Admin.NET `SysUpgradeService` 实现更新日志 CRUD、已读和未读提示，不是自动下载/部署程序的在线升级系统。

## 3. 基础后台逐功能差异

证据路径简写：`A` = 参考仓库 `Admin.NET/Admin.NET.Core/Service`；`F` = Full.NET `src/Modules`；`V` = `ui/admin/src/views`。表中方法名用于定位能力，不表示要复制参考 API。

| ID | 模块/功能点 | Admin.NET 功能事实 | Full.NET 当前实现与具体差异 | 判定 / 执行编号 |
| --- | --- | --- | --- | --- |
| ID01 | 账号认证 | Auth/Login、Logout、GetUserInfo | 登录/刷新/退出、Cookie/CSRF、锁定、TOTP 已有 | 已有；最终验收 |
| ID02 | 自助改密、锁定解除 | User/ChangePwd、UnlockLogin | 有管理员 reset-password；未见个人改密和独立解锁 API | 部分；14、15 |
| ID03 | 密码到期/首次强制修改 | User/VerifyPwdExpirationTime、参考登录响应及强制改密逻辑 | 未见到期/首次改密状态机与受限会话页面 | 缺失；16 |
| ID04 | 注册与手机号登录 | Auth/UserRegistration、LoginPhone、GetCaptcha | 当前是受控账号开通与密码/TOTP 登录；未见公开注册、短信登录、验证码产品入口 | 差异/可选；48 |
| ID05 | LDAP | Auth/SysLdapService 配置、认证、同步用户/部门 | 未见 LDAP 生产 Provider 与管理页 | 缺失；49 |
| ID06 | OAuth 外部账号 | OAuth/SignIn、SignInCallback、OAuthUser 管理 | 未见外部登录、绑定/解绑与外部账号管理 | 缺失；50 |
| ID07 | 用户基础管理 | User 增改删、启停、角色/扩展机构关系、导入导出 | 创建/编辑/启停/重置/角色、Excel 和组织关系已有；删除/退役入口未见，不能用禁用宣称删除能力已对齐 | 部分；17；删除策略另评审 |
| ID08 | 个人资料与附件 | User/GetBaseInfo、UpdateBaseInfo；File/UploadAvatar、UploadSignature | 用户档案管理已有；`/api/v1/me` 只读，未见自助资料/头像/签名写链路 | 部分；17、18 |
| ID09 | 敏感资料分级查看 | 最新 UserOutput 手机号/身份证号脱敏 | 已有字段级读写授权；HostUserProfileMapper 对有读取权者返回原值，缺少“掩码查看/明文揭示”分级 | 部分；13 |
| ID10 | 用户直授、注册渠道 | SysUserMenuService；SysUserRegWayService CRUD | 已有用户→角色授权；未见用户直授菜单和注册来源管理 | 差异/可选；48；直授先评估 RBAC 叠加语义 |
| RB01 | 角色/页面/操作授权 | Role CRUD、GrantMenu、GrantApi、GrantDataScope、GrantRoleTable | 已有角色、稳定权限树、数据范围、字段授权；不需要复制基于 URL/数据库表的授权 | 已有/差异；最终验收 |
| RB02 | 角色复制 | Role/CopyRole，最新提交强制取消系统内置标记 | 未见角色复制 Endpoint/UI | 缺失；19 |
| RB03 | 角色生命周期 | Role/SetStatus、DeleteRole、GrantUser | Full.NET 路由有 disable，没有对应 enable/delete；成员从用户侧分配，缺角色成员页入口 | 部分；20 |
| MN01 | 菜单与导航 | Menu 目录/页面/按钮 CRUD、状态，多级路由 | 有菜单、元数据、导航白名单及动作权限；多级路由是应复验项，本轮未确认存在功能缺陷 | 已有/待核；最终验收 |
| TN01 | 租户开通/套餐/切换 | Tenant 开通、状态、授权、用户、管理员密码 | 已有租户开通/更新/disable、套餐和可信切换；未见 enable/关闭生命周期、租户管理员操作一体化入口 | 部分；21 |
| TN02 | 租户品牌与系统信息 | Tenant/GetSysInfo、SaveSysInfo；Logo/Carousel | 当前租户更新主要为名称/版本，未见品牌、Logo、联系信息配置页/API | 缺失；22 |
| TN03 | 租户独立数据库 | Tenant/InitTenantDb、SyncTenantDb、GetTenantDbList | Full.NET 当前 SQL Server/MySQL 共享库租户隔离；独立数据库/在线初始化不在当前基线 | 差异；不自动实现 |
| OR01 | 机构/职位/职级/隶属 | Org 树、多机构，Pos CRUD，扩展机构岗位 | 已有树 CRUD、职位/职级、主机构/主职位与成员关系 | 已有；最终验收 |
| OR02 | 机构批量、职位 Excel | Org/BatchAddOrgs；Pos/Import、Export、DownloadTemplate | 未见机构批量导入和职位 Excel 产品入口 | 部分；24 |
| ST01 | 字典与配置 | Dict CRUD/状态；Config 分组、批量修改/删除 | Host/Tenant 字典及 Host 配置、分组/批量已有；不重复开发“配置分组” | 已有；最终验收 |
| ST02 | 枚举转字典 | Enum/EnumToDict、Const/GetData | 只读枚举目录已有；未见受控枚举→字典落地操作 | 部分；25 |
| ST03 | 列设置 | ColumnCustom/Store、Reset | 有 Grid 偏好 API/适配器；只定位到偏好模块与测试，未见业务表格可视化编辑器接入 | 部分；23 |
| AU01 | 访问/操作/异常/出站日志 | LogVis、LogOp、LogEx、LogHttp 查询/详情 | 对应日志查询页/API 和归档保留基础已有 | 已有；最终验收 |
| AU02 | 日志统计/导出/差异 | 年日统计、Excel 导出，LogDiff 变更详情 | 当前 QueryHost* Endpoint 主要为列表/详情；未见这些页面的受控统计/导出及独立变更差异查看链路 | 部分；26、27 |
| AU03 | 清空日志 | 多日志服务 Clear | 当前有保留/归档机制；不默认补“一键清空”，需审计保留策略明确授权 | 差异；27 只做授权导出 |
| OB01 | 文件日志 | LogFile 清单、流读取、下载 | 固定根、稳定 ID、有界读取和 Vue 页已有 | 已有；最终验收 |
| OB02 | 服务器监控 | Server/GetHardwareInfo、GetRuntimeInfo、GetNuGetPackageInfo | 有健康探针/遥测；缺实例 CPU/内存/运行时与版本信息管理页/API | 部分；28 |
| CA01 | 缓存控制面 | Cache 列 Key、查看、增改删、前缀删除、清空 | FusionCache 基础与失效治理已有；缺受控缓存运维页/API | 缺失；29；不开放任意写 Key/清空认证缓存 |
| ON01 | 在线会话 | 在线列表、强制下线、按用户下线、单端登录 | 会话列表与逐会话撤销已有；按用户撤销全部/单端策略未见独立管理能力 | 部分；30 |
| FL01 | 文件基本能力 | File 上传/列表/下载/删除 | Full.NET 有本地/S3、上传状态机、Reference Claim、补偿与删除 | 已有；最终验收 |
| FL02 | 文件目录/编辑/批量/预览 | File/GetFolder、UpdateFile、UploadFiles、GetPreview | 当前 Host Files 路由主要为单文件 CRUD/内容读取；缺业务目录、元数据编辑和批量操作入口，通用预览由 Document MVP 部分覆盖 | 部分；31、32 |
| FL03 | 对象存储厂商 | README OSS/COS 等扩展存储 | 本地、S3/MinIO 已有；未见 OSS/COS 专用适配与配置链路 | 部分；51；S3 兼容并不等于全部厂商能力已验证 |
| JB01 | 任务调度 | 定义、触发器、Cron、暂停/启动、执行记录/清理、集群 | Full.NET 已有定义/计划/历史、Cron 预览、HTTP Handler、集群健康、历史清理 | 已有；最终验收 |
| JB02 | 运行中取消、批量启停 | Job/CancelJob、PauseAllJob、StartAllJob、CancelSleep | 未见运行中取消请求与批量调度控制 API；静态 Handler 不等于动态 C# 作业 | 部分；33、34 |
| SC01 | 用户日程 | Schedule 用户日程 CRUD、时间过滤、状态 | 无用户日程模块/日历页；Jobs 不覆盖个人日程 | 缺失；35 |
| SR01 | 流水号 | Serial 规则、占位符、预览、分配、状态 | Host/租户分配、周期、幂等、分页筛选已有；当前明确余项主要是验证与审批产品接入 | 已有；02、最终验收 |
| CG01 | 代码生成 | 元数据、列控件、生成/预览、树与关系 | Full.NET 工作台、Vue SFC、双库草案、预览/Apply/Rollback、模块接线和下载已有 | 已有；最终验收 |
| CG02 | 元数据同步 | CodeGen/SyncCodeGen、数据库表列同步 | 已有导入与预览；尚需核实网页是否支持“刷新表结构并保留人工配置”的增量合并操作 | 待核；36 先核验再实现 |
| CG03 | 在线建表/动态编译 | Database/CreateEntity、ExecuteSQL；Plugin/CompileAssembly；ReZero | Full.NET 采用离线生成、显式编译部署、静态 AOT 闭包；线上任意 SQL/动态程序集属于有意差异 | 差异；52、53 只做受控替代 |
| UP01 | 更新日志 | Upgrade CRUD、已读、最新未读 | 未见更新日志/版本发布说明专用 API/页 | 缺失；37 |
| DB01 | 数据库目录 | Database 表列/视图/实体元数据 | CodeGeneration 已有只读表目录；缺独立视图/数据库检查产品面 | 部分；52 |
| DB02 | 备份管理 | DbBackup 计划/备份/文件下载/保留 | 部署文档不等于备份控制面；未见后台备份任务及结果目录页 | 缺失；54；恢复演练另行环境验证 |
| DB03 | APIJSON/通用存储过程查询 | APIJSON/Query、QueryByTable、Add、Edit、Delete；SysProc/ProcTable、CommonDataSet | Full.NET 无通用 JSON→任意表读写 API；业务查询必须保留模块/字段/租户授权，默认以静态查询和生成接口替代 | 差异；52 评估只读元数据，具体协议兼容须另立 Spec |
| RG01 | 行政区域 | Region 树/查询/CRUD/同步/生成机构 | 无 Regions 模块、数据版本管理和 Vue 级联管理页 | 缺失；38 |
| OA01 | 开放平台 | OpenAccess 客户端 CRUD、密钥、签名、日志 | API Key/HMAC 已有；缺独立接入方应用、细粒度授权/配额/签名调试与访问日志聚合产品面 | 部分；39、40 |
| MD01 | 模块管理 | Plugin CRUD、动态装载/移除 | Full.NET 只读模块目录已有；缺部署期模块启用策略查看/配置；热加载原实现不适用 AOT | 部分/差异；53 |
| LO01 | 多语言/时间 | Culture、客户端语言与时区显示 | BCP 47、用户语言、管理端词条及通知模板语言已有；逐模块新页面/组件翻译完整性待最终核验 | 已有/待核；最终验收 |
| DA01 | 主控面板 | 工作台、分析页、统计展示 | Overview 有真实摘要；时间范围趋势和聚合业务待办入口较少 | 部分；41 |
| IN01 | API 文档/限流 | Swagger、限流 | OpenAPI/Scalar、精确授权、生成客户端、限流已有 | 已有/差异；不为换 UI 再立项 |
| IN02 | Elasticsearch/MQTT/国密 | ES 日志、Mqtt 状态/客户端/发布/历史、SM2/3/4 工具 | 已有 OTel/Kafka/FusionCache 不等于以上 Provider；未见对应产品适配 | 缺失/按需；55、56、57 |
| IN03 | 压测与 SDK 生成工具 | Common/StressTest、GenerateAppApi、GenerateAllApi | Full.NET 已有 k6/容量工具和 OpenAPI 生成客户端；未提供后台任意压测或写入源码的接口，不据此判核心功能缺失 | 已有/差异；保持工具与管理 API 的运行边界 |

## 4. Workflow、表单、Notifications 与 DataApproval 明细

| ID | 功能点 | 参考能力 | 当前 Full.NET 与剩余功能 | 判定 / 执行编号 |
| --- | --- | --- | --- | --- |
| WF01 | 流程设计/版本 | Flow/SaveDesign、Definition 注册/版本 | 草稿/不可变发布版本、设计器、七种静态可执行节点已有 | 已有；不重做内核 |
| WF02 | 定义/表单生命周期 | Flow/Delete/SetStatus；Form/Unpublish/SetStatus；Definition/Delete/SetDefinitionStatus | 当前定义/表单端点集中在读、草稿、发布；缺停用/归档/受引用删除规则的操作闭环 | 部分；08、09 |
| WF03 | 发起/审批/抄送 | StartWorkflow、ApproveWorkflow、GetCcTodos | 发起、审批、抄送、已读已有 | 已有；最终验收 |
| WF04 | 我发起的/实例列表 | InstanceQuery/Page、MyInstances；myInitiatedTasks 页面 | 当前实例页要求输入 ID 查询；未见实例分页和“我发起的”查询端点 | 缺失；05 |
| WF05 | 待办/已办历史 | Todo/GetTodos、GetTodosHistory，pending/completed 页面 | `todos/mine` 为列表接口，没有专用分页筛选契约和已办工作台 | 部分；06 |
| WF06 | 业务标题/详情导航 | 最新 BusinessTitleTemplate、BusinessTitle 贯通实例/待办/已办；业务表单详情跳转 | Full.NET 未发现业务标题模板/快照；缺按可信业务类型跳转原表单的完整用户流程 | 缺失；07 |
| WF07 | 审批人选择 | 用户、角色、组织/上级部门规则 | 五类 resolver 已有；上级多层组织链、动态选择的具体需求先复核，不能把“当前部门负责人”当成完整上级链 | 部分；10 |
| WF08 | 会签/加签/退回 | 审批与拒绝策略、工作流 UI | all/any/nOfM、加签、退回已加入当前代码；复杂组合是否允许以发布校验和运行测试为准 | 已有；最终验收；超出现有支持的组合另立切片 |
| WF09 | 分支与运行控制 | 条件/并行，取消/暂停/恢复，步骤/日志 | 排他/并行/包容、暂停恢复、改派、超时、恢复任务已有 | 已有；最终验收 |
| WF10 | 可执行动态 SQL/任务节点 | Converter 识别 sql-node，部分 task-node 映射 Unknown | Full.NET 节点目录不提供任意 SQL/代码；参考设计器出现节点也不能证明它运行完整 | 差异/待核；仅经批准增加静态业务 Handler |
| FM01 | 表单基础组件 | formDes/formDesign，在线构建器 | Full.NET 12 类静态字段和运行时已有 | 已有；最终验收 |
| FM02 | 表单高级组件/产物 | 通用表单设计器的 Vue 产物及扩展控件目标 | 当前闭合目录不含附件、明细子表、远程字典联动；具体参考控件需在启动前验证其运行范围 | 部分/待核；11、12；独立 FormBuilder 仍需第二消费者 |
| NT01 | 公告与站内信 | Notice 发布/收到/未读/设置已读，Message 发信 | Host 公告发布/撤回、Host/Tenant Inbox、read-all/SignalR 已有；公告专用收到/已读统计产品面未见 | 部分；42 |
| NT02 | 消息平台 | 邮件/短信、消息日志 | 模板、Intent、Profile、Binding、Worker、重试、投递页已有 | 已有；最终验收 |
| NT03 | 邮箱验证/模板语言 | 通知投递相关能力 | 验证码发送/验证、语言模板已在近期切片实现；不能沿用旧“未实现”标记 | 已有；外部环境验证留后 |
| NT04 | 邮件附件/格式 | 新增 MailAttachment、SendEmailToMultiple | 当前 SMTP 文本邮件；命令未提供附件，缺从 Files 受控引用附件的端到端链路 | 部分；43 |
| NT05 | 回执/退信 | 外部投递状态管理 | 有回执接收器、状态机和 receipts 时间线；生产源码未发现实现 INotificationReceiptVerifier 的厂商验签器，SMTP descriptor 为 none | 部分；44；有通用 Webhook ≠ 真实邮件送达回执可用 |
| NT06 | 短信 | Aliyun/Tencent/Custom、模板/验证码 | 未见短信生产 Adapter；邮箱验证码不能视为短信能力 | 缺失；45 |
| NT07 | 企微/钉钉/微信渠道 | 消息、卡片、模板/订阅推送 | 通用 Profile/Binding 不能代替渠道实现；渠道适配仍缺 | 缺失；58–61 |
| AP01 | 数据审批首场景 | DataApproval 流配置/表单路由/匹配审批 | Full.NET 已有流水号规则 UPDATE 请求→Workflow→业务应用，列表/详情/取消 | 部分；不从零建模块 |
| AP02 | 审批场景配置 | 参考 Code/Name/FlowJson/FormJson、FormRoutes、MatchApproval | 当前场景固定，缺受控场景目录、规则与已发布流程绑定管理 | 缺失；01 |
| AP03 | 业务操作发起审批 | 表单路由对应审批流程 | 当前页输入目标 Guid、流程 Key 和 JSON；业务规则编辑页未形成强类型“提交审批”和字段差异体验，原直接修改仍可用 | 部分；02 |
| AP04 | 启动/应用失败恢复 | 业务完成/拒绝/取消联动目标 | 当前创建重放可恢复启动；缺独立待关联扫描/重试/应用冲突处置 UI 与后台恢复闭环证据 | 部分；03、04 |
| AP05 | 多业务场景 | 可配置业务表单与审批规则 | 当前仅单个流水号更新；第二真实场景、Tenant 场景、Create/Delete 审批均未完成 | 部分；46；后续每个真实场景独立立项 |

## 5. Document、数据工具与所有业务插件

| ID | 模块/功能点 | Admin.NET 具体能力 | Full.NET 当前与缺口 | 判定 / 执行编号 |
| --- | --- | --- | --- | --- |
| DC01 | 文档基本能力 | 文档 CRUD、分类、标签、权限、分享、回收站、统计 | Host 文档上述入口及版本上传/历史、MVP 预览已有 | 已有；最终验收 |
| DC02 | 版本回滚/删除 | DocumentVersion/Rollback、DeleteVersion | 当前 Endpoint 有版本查询/新增/预览，缺版本回滚和独立版本删除/保留控制 | 部分；47、62 |
| DC03 | 文档附加功能 | 热门/推荐标签、名称冲突、文档访问统计/日志、批量分享 | 全局统计与单个分享已有；这些具体运营入口未完整对齐 | 部分；63 |
| DC04 | Office 预览 | DocumentPreview/GetPreviewUrl 及转换能力 | 当前 MVP 预览，Office→PDF 是已记载的产品差异；不要当成已有 | 差异/可选；64 |
| DC05 | Tenant 文档 | 参考多租户框架下的文档服务 | Full.NET 管理路由明确 Host；是否需要租户文档需先确定业务范围 | 差异/待核；64 前先确定作用域，不默认复制 |
| IE01 | 通用导入导出 | Magicodes.IE，工作表配置、模板、错误文件、职位导入等 | Identity 固定 Excel 已有；缺跨模块受控任务、工作表配置、预校验/分批执行/结果下载 | 部分；65、66；24 消费已有能力时可先做小切片 |
| RP01 | 报表数据源 | ReportDataSource CRUD/连接串加密/列表脱敏 | 无 Reporting 生产模块；需凭据引用、数据源权限和连接测试 | 缺失；67 |
| RP02 | 报表分组/定义/查询 | ReportGroup、ReportConfig/Copy/ParseSql/ExecuteSqlScript/GetLayoutConfig | 无定义、参数、授权查询、布局与分页结果页面 | 缺失；68、69 |
| RP03 | 报表导出/HTML/PDF | ExportToExcel、H5 模板报告 | 未见通用大结果导出与 HTML/PDF 任务 | 缺失；70 |
| PR01 | 打印 | Print 模板 CRUD/复制、前端打印设计器 | 无打印模板/API/预览/打印客户端 | 缺失；71 |
| AI01 | AI 对话 | LLM 模型选择/切换、流式聊天、历史删除/重命名 | 无 AI 模块/页面/生产供应商 | 缺失；72、73 |
| AI02 | Agent/Tool/MCP | 参考 MCP 服务与工具暴露；路线图还包含 Agentic Web | Full.NET 无工具服务；参考有 MCP 不代表已验证完整 Agent/配额/人审产品 | 缺失/范围待定；74 |
| DD01 | 钉钉插件 | Token、互动卡片、创建投递、审批实例接口 | 无对应 Provider | 缺失；58、59 |
| WW01 | 企业微信插件 | 部门/用户/标签 CRUD、成员邀请/转换、应用群聊及多消息类型 | 无组织同步与消息 Provider | 缺失；60 |
| WC01 | 微信公众号/小程序 | OAuth/OpenId、手机号、模板/订阅、客服消息、二维码 | 无对应 Provider/业务入口 | 缺失；61 |
| PY01 | 微信支付 | 下单/Native/JSAPI/服务商、查询、回调、退款与列表 | 无 Payments 模块 | 缺失；75、76 |
| PY02 | 支付宝 | 网页支付、预下单、通知、转账 | 无对应 Provider | 缺失；77；转账需独立资金安全切片 |
| GV01 | GoView | 项目 CRUD、画布保存、发布、上传、背景图、预览 | 无大屏项目/API/编辑客户端 | 缺失；78 |
| K301 | K3Cloud | ValidateUser、Save、Submit、Audit | 无适配器；参考主要是接口封装，不等于完整 ERP 模块 | 缺失；79 |
| OC01 | PaddleOCR | IDCardOCR | 无身份证识别 Provider；不据此扩大到全部票据/文档 OCR | 缺失；80 |
| RZ01 | ReZero | 动态接口、模板、SuperApiAop、线上建模目标 | Full.NET 无运行时动态 API；用 CodeGeneration 静态发布或隔离 Compatibility 评估 | 差异；52、53；未批准前不创建动态执行器 |
| MQ01 | MQTT | 状态、客户端列表、消息发布/分页/清理 | Kafka 是内部事件交付，不能替代 MQTT 设备/消息协议 | 缺失；56 |
| CL01 | Web/第二后台 | Web Vue3、Web_Artd | Full.NET Vue 主交付线已有；Layui 冻结，不补第二套后台 | 已有/差异 |
| CL02 | H5/小程序 | App 资产、移动流程/业务交互 | uni-app 登录/消息/Workflow 等部分切片已有；微信/支付宝目标平台功能与发布证据未齐 | 部分；81 |
| CL03 | 原生/桌面 | Web_Desktop/原生交付需求 | Flutter 当前无可对应完整产品交付证据，路线图为 Designing | 待建设；82；独立客户端需求启动 |

## 6. 关键源码证据入口

这些入口同时解释了为何旧路线图不能直接作为本轮差异结论：

| 证据 | 位置与观察 |
| --- | --- |
| 参考 Core 总目录 | [Admin.NET Core Service](../../../Admin.NET.Pro/Admin.NET/Admin.NET.Core/Service)：逐类方法清点；包含日程、更新日志、报表、LDAP、OpenAccess 等 README 未细分项 |
| 参考插件总目录 | [Admin.NET Plugins](../../../Admin.NET.Pro/Admin.NET/Plugins)：十个业务插件逐一核对；MySQL WorkflowCore 持久化项目不另算产品模块 |
| 新增业务标题 | [WorkflowFlow](../../../Admin.NET.Pro/Admin.NET/Plugins/Admin.NET.Plugin.WorkFlow/Entity/WorkflowFlow.cs)、[WorkflowInstance](../../../Admin.NET.Pro/Admin.NET/Plugins/Admin.NET.Plugin.WorkFlow/Entity/WorkflowInstance.cs) |
| 参考更新日志 | [SysUpgradeService](../../../Admin.NET.Pro/Admin.NET/Admin.NET.Core/Service/Upgrade/SysUpgradeService.cs)：确认它不是在线部署升级器 |
| 用户 API/字段投影 | [Endpoint](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs)、[HostUserProfileMapper](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserProfileMapper.cs) |
| 角色 API | [Endpoint](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/Endpoint.cs)：未见复制/启用/删除路由 |
| 实例 API/页面 | [Endpoint](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/Endpoint.cs)、[WorkflowInstancesView](../../ui/admin/src/views/WorkflowInstancesView.vue)：当前按 ID 查详情 |
| 待办 API | [Endpoint](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageMyTodos/Endpoint.cs)：列表契约没有分页/历史筛选参数 |
| 节点/表单闭合目录 | [WorkflowNodeTypeCatalog](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowNodeTypeCatalog.cs)、[WorkflowFormComponentCatalog](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowFormComponentCatalog.cs) |
| DataApproval 当前闭环 | [DataApprovalRequestService](../../src/Modules/Full.NET.Modules.DataApproval/Features/ManageRequests/DataApprovalRequestService.cs)、[DataApprovalRequestsView](../../ui/admin/src/views/DataApprovalRequestsView.vue) |
| SMTP 边界 | [SmtpNotificationProviderAdapter](../../src/Modules/Full.NET.Modules.Notifications/Providers/Smtp/SmtpNotificationProviderAdapter.cs)：文本邮件与 none 回执能力；生产无具体 INotificationReceiptVerifier 实现 |
| Document 版本功能 | [Endpoint](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/Endpoint.cs)：有版本新增/读取/预览，无版本回滚路由 |
| Grid 偏好 | [grid-preferences](../../ui/admin/src/preferences/grid-preferences.ts)：有契约适配器，未找到业务页面编辑器消费 |
| 历史总体范围 | [功能对标路线](../roadmap/adminnet-feature-parity.md)、[能力矩阵](../roadmap/capability-status.md)：用于范围参考，滞后项由本报告标注，不据其历史描述否定新代码 |

## 7. 可执行清单：公共规则

本清单是可交给 Cursor 的候选任务卡，当前只输出清单，不开始实现。新模块仍遵守既有 G4/Spec 门禁；“可执行”表示边界、产物与验收已明确，不表示所有第三方接入/资金操作已获授权。

每次只执行一个编号，独立 `codex/` 分支、独立提交，完成后停止并汇报。以实际目标分支为起点并检查工作区；此前恢复测试提交仍在独立分支，不能假定已合并到 main/master。分支合并、推送按当次明确授权处理。

每个功能编号同时交付服务端/API 与对应 Vue 功能页面/操作。单元、权限、租户、数据一致性、公开契约、双库迁移及恢复测试属于当次交付要求，不能留到最后。页面级真实栈 E2E、视觉调整、可访问性集中检查和人工逐页验收放在所选功能批次末尾。

统一完成条件：

- 从受信身份确定用户/租户；页面和操作均有稳定精确权限，403/404/409 及空态可操作。
- 公共 JSON/OpenAPI/生成客户端、AOT 源生成、成对迁移及恢复验证同步；中文注释按仓库规则。
- 本地先编译、静态、Unit/Architecture/Vitest/typecheck；环境重型双库/原生测试按影响集进入已授权 CI，不把“已写测试”说成“数据库通过”。
- 新模块只建实际需要的主项目；跨模块读用 Port、写用可靠事件/幂等/对账；不开放任意 SQL、文件路径、动态代码执行。
- 后端或前端原有功能已经满足目标时，只补缺口/证据，不重新实现。遇到表中“待核”先给出源码证据再决定代码变更。
- 新模块/高风险能力启动时先在本编号内完成明确设计；若涉及尚未批准的架构或厂商选择，产出具体 Spec 后停在相应边界，不用空壳页面冒充完成。

### A. 当前产品主线（01–12）

| 编号 | 后端/API 交付 | Vue 交付 | 关键验收 / 依赖 |
| --- | --- | --- | --- |
| 01 | DataApproval 静态场景目录和场景→发布流程版本的受控绑定；首批仅已有流水号场景 | 场景列表、配置/启停绑定、选择发布版本 | 未登记场景拒绝、Host/Tenant 不串、历史请求固定版本；复用现有模块 |
| 02 | 流水号更新审批强类型提案/字段差异；开启审批策略后直接写入口也执行策略 | 规则编辑页“提交审批”、变更前后对比、跳转请求详情 | 依赖01；拒绝不落业务写入、批准版本冲突失败关闭；不全局强制审批 |
| 03 | 待启动/待关联请求的有界扫描、稳定幂等重试与人工重试端点 | 请求页恢复状态/失败原因/重试按钮 | 依赖01；启动后进程退出、关联 CAS 冲突、重复投递无第二实例 |
| 04 | 批准后业务应用的持久化结果/恢复与冲突处置记录 | 应用失败/冲突详情、授权重试入口 | 依赖02、03；重启/多实例/版本变化不误标 approved，不静默覆盖目标 |
| 05 | 实例分页、状态/时间/定义筛选、“我发起的”可信用户查询 | 实例列表及“我发起的”页签，链接现有详情 | 全局管理和个人读取分别授权；排序稳定、租户隔离、分页上限 |
| 06 | 待办/已办历史分页筛选，保留历史动作快照 | 待办/已办页签、筛选与分页，详情可回看 | 依赖05；已办列表不依赖当前待办状态覆盖历史，用户只看授权范围 |
| 07 | 版本化业务标题模板、启动时标题快照、可信业务详情标识 | 发起/待办/已办/实例标题与“查看业务单据” | 依赖05、06；不执行模板代码、不任意跳 URL，不因业务后续修改漂移标题 |
| 08 | 流程定义启停/归档、受引用版本删除规则 | 定义和版本操作入口 | 停用阻止新实例且旧实例继续；运行引用版本不得删除 |
| 09 | 表单停用/归档与发布撤回的受引用保护 | 表单列表操作及引用提示 | 依赖08；已绑定运行实例仍可读取原表单版本 |
| 10 | 在已有五类 resolver 上补经确认的上级组织层级解析 | 节点办理人规则编辑与范围预览 | 先核实参考实际规则；空集合失败关闭、循环组织拒绝、启动快照一致 |
| 11 | FormSchema 附件字段、Files Claim 与运行时读写授权 | 原生设计器附件配置、运行时上传/查看 | 审批字段权限、跨租户附件拒绝、删除引用保护；保留静态闭包 |
| 12 | FormSchema 有界明细子表与服务端校验 | 设计器子表配置、运行时增删行/错误定位 | 依赖11非强制；行数/嵌套上限、不可写字段不可绕过；其余高级控件另拆 |

### B. 已有后台模块补齐（13–47）

| 编号 | 后端/API 交付 | Vue 交付 | 关键验收 / 依赖 |
| --- | --- | --- | --- |
| 13 | 手机号/证件号掩码投影与独立明文揭示权限/审计 | 用户列表默认掩码、按权查看，编辑保留原值不提交掩码 | 列表/导出/详情一致，无权直接 API 拒绝；基于既有字段授权 |
| 14 | 当前用户改密、旧密码验证、会话失效规则 | 安全设置改密表单 | 错旧密码拒绝、新旧令牌边界、CSRF/限流、密码不入日志 |
| 15 | 管理员解除登录锁定的精确权限 API | 用户页锁定状态与解锁操作 | 不借解锁启用已禁用用户，留审计，多实例状态一致 |
| 16 | 初次/重置后强制改密状态、可配置到期策略 | 登录后受限改密流程 | 依赖14；未改密不能调用普通业务 API，refresh 不能绕过 |
| 17 | 当前用户自助资料更新与只读敏感字段界限 | 个人资料页 | 复用既有权威校验；不可改租户/角色/启停状态 |
| 18 | 头像/签名受控文件关联、类型/体积校验 | 个人资料上传/删除/预览 | 依赖17；使用 Files Port/Claim，不任意路径/URL |
| 19 | 角色复制权限/数据范围/字段授权，系统标记不得继承 | 角色页复制对话框和结果 | 源权限交集可授权、名称冲突回滚、不复制超级管理员身份 |
| 20 | 角色启用、退役/受引用删除及角色成员管理 | 角色状态/成员页签 | 依赖19非强制；最后管理员/成员引用保护、撤权生效 |
| 21 | 租户重新启用及受控关闭/停用完整状态；管理员操作复用 Identity 端口 | 租户生命周期与成员/管理员入口 | 不级联删业务数据、不绕最后管理员保护；独立库不在范围 |
| 22 | 租户品牌/联系信息强类型配置，Logo 用 Files 关联 | 租户品牌设置和运行时消费 | 依赖18可复用；隔离、服务端验证、禁止配置可执行客户端路径 |
| 23 | 为真实用户列表补列目录/偏好契约所需缺项 | 显示/隐藏/排序/固定/重置列编辑器并接入 UsersView | 只操作已授权列；偏好不能打开被字段权限隐藏的数据 |
| 24 | 职位 Excel 模板/预校验/导入导出，机构批量作为下一独立子切片 | 职位文件操作与逐行结果 | 复用 Identity 文件限制经验，重复/越权/跨租户引用拒绝；不在同提交加全部模块导入 |
| 25 | 已登记静态枚举转字典的预览/确认写入 | 枚举页“生成字典”、冲突预览 | 幂等、翻译不作机器码、不覆盖人工字典 |
| 26 | 审计时间范围聚合、变更字段差异的脱敏只读查询 | 日志趋势和详情差异页签 | 数据范围、时间桶/行数上限；历史没有前后值时如实提示 |
| 27 | 日志受控导出任务或有界导出，复用归档保留 | 日志导出/结果下载 | 依赖26非强制；字段授权、公式注入/文件边界、导出审计；不新增任意清空 |
| 28 | 实例目录、运行时/内存/CPU/版本只读信息 | 服务器监控页 | 不暴露环境变量/连接串；实例身份明确，跨平台不可用值如实表达 |
| 29 | 已登记缓存命名空间/策略的查看与精确失效操作 | 缓存管理页 | 凭据/值脱敏、有界目录、不 SCAN 全库/全库清空；复用 FusionCache 失效语义 |
| 30 | 按用户撤销全部会话及显式单端登录策略 | 会话页按用户批量下线/策略提示 | 不撤销错误用户，refresh/SignalR 同步失效，操作审计 |
| 31 | Files 虚拟目录/元数据修改与受控引用查询 | 文件目录浏览/编辑 | 虚拟目录不是磁盘路径；版本冲突/跨模块归属正确 |
| 32 | Files 批量上传/结果、按授权批量删除与安全预览 | 上传队列、逐文件反馈和预览入口 | 依赖31非强制；逐项失败明示、引用中对象不删除 |
| 33 | Jobs 运行中取消请求、Worker 合作取消及状态确认 | 执行页取消按钮与“取消中/已取消/不支持” | 不把请求取消当成业务回滚；lease/并发终态一致 |
| 34 | Jobs 批量暂停/恢复的有界操作 | 调度页批量操作与逐项结果 | 不改变正在执行任务含义；权限、失败列表、乐观锁 |
| 35 | 用户日程 CRUD/状态/时间范围查询，单一真实归属模块 | 日历/列表/编辑页 | 当前用户所有权、时区边界；提醒作为后续 Notifications 集成 |
| 36 | 核实元数据增量同步现状；缺失时补刷新预览/冲突合并 API | 生成工作台“同步表结构”与差异选择 | 不丢人工列配置、不直接 Apply；已有功能只补测试/入口 |
| 37 | 更新日志发布/编辑/撤回与用户已读记录 | 更新日志管理、最新未读提示 | 不做在线部署器；版本排序、已读幂等和权限 |
| 38 | 行政区域树/查询/CRUD、版本化种子或导入 | 区域管理/级联选择 | 数据来源固定、同步差异可审查；不直接调用未确定地图厂商 |
| 39 | OpenAccess 客户端应用、密钥轮换/停用与权限绑定 | 接入方应用管理页 | 复用 API Key/HMAC、密钥只显示一次、权限交集、撤销即时生效 |
| 40 | 接入方访问记录/配额与有界签名调试 | 应用详情日志/用量/调试页签 | 依赖39；不回显服务器密钥，不跨应用读取日志 |
| 41 | 工作台时间趋势与业务入口聚合 | 工作台统计范围/图表/待办入口 | 依赖05、06；只聚合有权指标，经模块 Port 批量读取 |
| 42 | 公告接收列表、已读记录、授权已读统计 | “我收到的公告”、发布方统计 | 当前 Host 公告范围先闭合，Tenant 公告另明确范围 |
| 43 | SMTP 邮件附件的 Files 引用与有界装载；需要时扩展安全 HTML 模板 | 模板/发送入口附件选择和结果 | 文件权限/大小/类型、撤权、队列重试；厂商凭据验收不冒充本地通过 |
| 44 | 为选定真实邮件服务增加回执验签/消息 ID 映射，或明确 SMTP 无回执能力 | 渠道能力与回执支持状态展示 | 先确定可提供回调的服务；重复/乱序/伪造拒绝，不把 Accepted 当 Delivered |
| 45 | 选定一家短信 Provider 的模板发送/验证码/回执闭环 | 短信 Profile/模板/测试与投递详情 | 厂商选择及账号为前置；复用通知 Worker、限流、防重复消费 |
| 46 | 第二个真实 DataApproval 场景的 Source/Applier 与策略接线 | 对应业务表单提交审批 | 依赖01–04；由真实业务需求确定对象，不生成万能 JSON 审批器 |
| 47 | Document 版本回滚为新的当前版本/受控指针变更 | 历史版本回滚操作 | 权限、Files Claim、并发与审计；历史版本不可静默覆写 |

### C. 按业务需要启动的扩展（48–82）

这部分不插队当前 DataApproval 收口。每个编号仍交付完整小切片；第三方平台先选定首个供应商/账号，功能边界大于一次独立提交时先产出该编号的分解方案，不能把整个平台塞进一个提交。

| 编号 | 可交付切片（后端/API + 对应 Vue） | 关键验收与停止边界 |
| --- | --- | --- |
| 48 | 受控注册政策和注册来源管理；短信登录单独后续切片 | 依赖45才启短信登录；不默认开放公网注册，验证码/风控服务明确后实施 |
| 49 | LDAP 连接配置、认证测试和只读同步预览页 | 凭据不回显、目录范围白名单；确认映射后再做有审计的同步应用 |
| 50 | 首个 OAuth/OIDC Provider 登录回调和绑定/解绑页面 | state/nonce/PKCE、账号冲突、不能按未验证邮箱自动合并 |
| 51 | 首个非 S3 存储 Provider 及存储管理能力展示 | 先选 OSS 或 COS 一家；保留旧 ProviderKey 读取与补偿，不批量迁移旧文件 |
| 52 | 数据库只读表/视图目录与元数据检查页、生成迁移草案 | 不运行任意 SQL/在线改表；复用 CodeGeneration 元数据，不扩大跨模块查询权 |
| 53 | 构建/部署期模块启用配置的校验 API/管理预览页 | 依赖 DAG 与 AOT 闭包；应用需走部署流程，不动态加载程序集 |
| 54 | 授权备份执行器的任务目录/结果/受控下载页面 | 执行环境/凭据/对象存储前置；生产恢复操作不在该编号内自动执行 |
| 55 | 经现有日志管道到 Elasticsearch 的单一适配与健康页 | 确认 OTel exporter 是否已满足需求，避免重复采集；不把 ES 强耦合业务模块 |
| 56 | MQTT 首个受控发布场景、客户端/消息记录查询页 | 主题/租户 ACL、消息大小/速率/幂等；不是 Kafka Provider 换名称 |
| 57 | 国密首个真实签名/验证用例与密钥状态管理页 | 先选合规实现及调用目的；不暴露匿名通用解密接口 |
| 58 | 钉钉消息/互动卡片 Adapter 和 Profile/投递页配置 | 固定首种卡片、token 续期、权限和失败回执；不同时建设整个组织系统 |
| 59 | 钉钉单个审批实例同步场景与同步记录页 | 依赖58非强制；签名/幂等/补偿，避免双系统同时成为流程权威 |
| 60 | 企业微信文本通知 Adapter 与 Profile/投递页面 | 部门/用户/标签同步及群聊其他媒体各另拆编号，不在本提交全包 |
| 61 | 微信小程序 OpenId 绑定与订阅消息首切片页面 | 确认 AppId；公众号客服/素材/二维码后续独立；验证身份与订阅授权 |
| 62 | Document 历史版本保留策略和授权删除入口 | 依赖47；当前版本/被引用版本不删，清理保留审计和存储 Claim 一致性 |
| 63 | Document 访问日志/统计页签；首批仅一个可验证指标集 | 热门标签/批量分享等按本行差异另拆后续切片，不混入统计提交 |
| 64 | 独立 Office→PDF 转换 Provider 和文档预览任务页 | 文件隔离/资源上限/字体/许可证；Tenant 文档需另明确作用域，不顺带实现 |
| 65 | ImportExport 首个静态 Schema 任务：模板/工作表选择/预校验 API 与任务页 | 先定 Spec；流式/大小/行数上限、禁止公式执行，首个消费者可选职位 |
| 66 | ImportExport 分批执行、错误回执下载、断点/重试 UI | 依赖65；模块拥有实际写入，部分成功/失败可解释，不跨模块本地事务 |
| 67 | Reporting 数据源安全配置/测试 API 与数据源页 | 先定 Spec；加密/Secret 引用、只读身份、列表脱敏，不通过任意连接串越界 |
| 68 | Reporting 分组/定义/参数 Schema/发布版本与编辑页 | 依赖67；从静态 Query Port 或受审查只读查询开始，禁任意客户端 SQL |
| 69 | Reporting 有界执行/分页/取消/超时与参数结果页 | 依赖68；列权限、租户、失败即停，数据库只读凭据仍不代替业务授权 |
| 70 | Reporting 首种导出（先 Excel 或 HTML）及导出任务页 | 依赖69；PDF 渲染另拆后续编号；背压、大小、模板注入、下载授权 |
| 71 | Printing 一个业务表单的模板版本/数据绑定 API 与预览打印页 | 依赖68可选；固定字段 Schema、浏览器打印/导出边界；不默认接硬件打印服务 |
| 72 | AI 首个模型配置/连通性/额度 API 与管理页 | 独立 Spec；供应商中立、Secret 不回显、租户配额、不建立虚假通用供应商 |
| 73 | AI 单一聊天流程：流式回复/取消/历史和 Vue 对话页 | 依赖72；用量计费边界、错误恢复、敏感信息策略 |
| 74 | 首个只读 Agent Tool/MCP 静态目录和工具调用审计页 | 依赖72与独立安全 Spec；不转发调用者 Header 走本机 HTTP 回环；写工具人审另拆 |
| 75 | Payments 首个商户/渠道配置与支付单创建/查询页面 | 独立资金安全设计与凭据前置；先只选微信一种交易模式 |
| 76 | 微信支付回调幂等/对账、单笔退款与状态管理页 | 依赖75；签名/金额/商户校验，退款与对账可再拆独立提交，禁止真实自动扣款测试 |
| 77 | 支付宝单一交易模式与支付/查询页面 | 依赖75通用订单边界；转账不随网页支付一起授权或实现 |
| 78 | GoView 首个大屏项目保存/发布/只读预览 API 与编辑客户端 | 独立 Client/Spec；数据源权限与发布快照，禁大屏绕过业务 API 查询 |
| 79 | K3Cloud 首个固定单据的认证/提交适配和同步状态页 | 指定单据为前置，Save/Submit/Audit 按实际流程逐切片；不做万能远程方法调用 |
| 80 | OCR 身份证识别 Provider 与受控上传/结果确认页 | 依赖Files；识别结果不自动变成权威档案，留人工确认/最小保留 |
| 81 | uni-app 首个选定平台的既有审批/消息链路功能缺项 | 先选微信小程序或支付宝小程序；不一次承诺所有平台，页面验收在批次末尾 |
| 82 | Flutter 首个明确业务场景的 API 契约消费/客户端页面 | 原生/桌面平台选择与发布要求前置；不复制整个后台，不先堆空项目 |

上述 C 区对范围大的模块给的是“首个可执行切片”，不是该模块全量对标完成承诺。后续切片必须继续从差异表逐项销项。例如企业微信组织/标签/群聊、微信客服/二维码、打印设计器、AI 写工具等，不能在首切片完成后标记模块全量完成。

### D. 页面验收放在所选功能批次最后

不是等所有可选的支付/AI/桌面模块建设完才验收当前后台。先确定本轮要做的 A/B 编号集合；这批功能实现完成后进入以下末尾任务，每次仍只执行一个编号。

| 编号 | 末尾任务 | 验收证据 |
| --- | --- | --- |
| 83 | Identity/角色/菜单/租户/组织/设置页面真实栈与逐页验收 | 双 Provider 关键流程、撤权/直调403、空错态、视觉和键盘操作 |
| 84 | Workflow/表单/待办/已办/实例/恢复页面验收 | 分支与审批组合、历史快照、并发反馈、原表单跳转、人工审阅 |
| 85 | Notifications/DataApproval 业务链路页面验收 | 提交→待办→审批→业务应用→通知；拒绝/撤回/失败恢复；外部账号证据单列 |
| 86 | Files/Document/Jobs/CodeGeneration/运维及所选新模块页面验收 | 上传/引用/回滚/取消/Apply 等各自真实流程，逐页面记录未通过项 |
| 87 | 目标提交门禁和对标状态收口 | 核对 CI 双库、迁移恢复、AOT；检查无越界 Verified；容量/生产回执/灾备单独标状态 |

## 8. 推荐近期顺序与给 Cursor 的通用提示词

优先执行 01–09，补齐数据审批业务闭环和流程用户入口；随后 13–23、28、43、47。10–12 和其余 B 项按当前真实需求插入；65–71 在 DataApproval 主线收口后进入新模块波次。外部厂商能力按需求选择，不默认一次性启动。

以下提示词直接附在本报告后，替换 NN：

```text
本次只执行《Admin.NET.Pro 逐模块功能差异与执行候选清单（2026-09-05）》编号 NN。

先读取仓库 AGENTS.md、rules/README.md 和对应模块规则/Skill；
核对当前源码与报告固定基线之间的变化，确认该项尚未完成。
检查当前分支和工作区，按真实目标分支创建独立 codex/ 分支并记录基线，
保留所有无关变更；报告中的编号不代表此前完成的 14 个任务。

交付本编号限定的后端/API、对应 Vue 页面/操作、生成客户端和精确权限。
涉及数据变更时同步 SQL Server/MySQL 迁移与部分状态恢复测试。
保持模块数据所有权、租户隔离、事务/幂等、中文注释和 Native AOT 静态边界。
按风险先建立可失败验证，完成本地编译、静态检查、单元/架构、
Vue 单测与类型检查；双库和 Linux 原生等重型验证按受影响集交给已授权 CI。
不把这些安全/数据/契约验证推迟到页面验收阶段。

页面级真实栈 E2E、视觉微调、可访问性集中检查与人工逐页验收
统一留到本轮所选功能建设结束后。本编号仍必须有可用的 Vue 功能，
不得只交付后端、空页面或要求业务用户直接输入内部 Guid/任意 JSON。

如果本项需要新模块 Spec、供应商选择或不同于既有架构的决策，
先完成具体可审阅设计并明确剩余前置；不要假定该报告批准了新架构。
若源码已具备某子能力，引用证据并只补真实缺口。

完成本编号后独立提交并停止。
汇报：后端/API、Vue、测试命令与实际结果、分支/提交、
尚未执行的 CI/页面验收项及阻塞；不得提前做后续编号。
```

## 9. 未验证范围与维护

- 本次为静态功能盘点，不是安全漏洞审计、完整逐行代码审查或运行验收；疑似缺少的接口按已扫描源码范围表述。
- 没有计算“完成百分比”：模块和子功能权重不相等，且存在有意差异与厂商环境门槛。
- “已实现但未验证”不计为缺失开发功能；C 区的首切片也不代表其模块全量完成。
- 本次只新增此交接报告，不改生产代码、页面、迁移、已有路线图状态，不提交或合并任何分支。
- 规则/Skill 演进：现有能力映射、静态 AOT 与文档分层规则可覆盖本任务，没有新增规则或 Skill。
