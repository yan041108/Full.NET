# Admin.NET.Pro 功能对标路线

- 基线仓库：`G:\wwwroot\github_fork\Admin.NET.Pro`
- 基线分支：`v2.1`
- 建立日期：2026-07-17
- 目标：Admin.NET.Pro 的适用功能原则上在 Full.NET 中全量对标

## 1. 对标定义

全量对标以业务能力、关键用户流程和项目交付价值为验收对象，不要求复制 Admin.NET.Pro 的源码、表结构、API 路径、Furion/SqlSugar 使用方式或工程拆分。

每项能力必须归入以下一种交付形态：

- `Core`：Full.NET 1.0 默认基础能力；
- `Official Module`：官方维护、按需安装的业务模块；
- `Provider`：第三方平台或基础设施适配器；
- `Compatibility`：迁移旧 Admin.NET 项目所需的兼容层；
- `Sample`：强行业属性的参考实现；
- `Client`：管理端、移动端、桌面端或可视化客户端。

状态定义：

- `Mapped`：已确定 Full.NET 归属，尚未实施；
- `Designing`：正在形成模块规格；
- `Implementing`：正在开发；
- `Implemented`：功能完成，尚未通过完整对标验收；
- `Verified`：功能、关键流程、权限、租户和测试均已验收；
- `Not Applicable`：经设计评审确认不适用，并已记录替代方案。

功能对标完成的唯一标准是状态为 `Verified` 或经批准的 `Not Applicable`。

## 2. README 内置功能基线

| Admin.NET.Pro 功能 | Full.NET 归属 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| 主控面板、工作台、分析和统计 | `ui/admin` + Dashboard Contracts | Client | M3 | Mapped |
| 用户管理 | Identity | Core | M2 | Mapped |
| 机构管理 | Organization | Core | M2 | Mapped |
| 职位管理 | Organization | Core | M2 | Mapped |
| 菜单与按钮权限 | Identity | Core | M2 | Mapped |
| 角色与数据授权 | Identity + Organization | Core | M2 | Mapped |
| 字典管理 | Settings | Core | M3 | Mapped |
| 访问日志 | Auditing | Core | M3 | Mapped |
| 操作与异常日志 | Auditing | Core | M3 | Mapped |
| 服务监控 | Observability Admin | Official Module | M5+ | Mapped |
| 在线用户与强制下线 | Identity + Notifications | Core | M2 | Mapped |
| 公告与 SignalR 通知 | Realtime + Notifications | Core | M2/M3 | Mapped |
| 文件与对象存储 | Files + Storage Providers | Core + Provider | M3/M5+ | Mapped |
| 任务调度 | Jobs | Core | M3 | Mapped |
| 系统配置 | Settings | Core | M3 | Mapped |
| 邮件与短信 | Notifications Providers | Provider | M5+ | Mapped |
| Swagger、OpenAPI 和接口文档 | Hosting | Core | M1 | Mapped |
| 前后端代码生成 | CodeGeneration | Core | M3 | Mapped |
| 在线表单构建器 | FormBuilder | Official Module | M5+ | Mapped |
| 微信小程序与微信支付 | WeChat + Payments | Official Module + Provider | M5+ | Mapped |
| Excel 导入导出、HTML/PDF 报告 | ImportExport + Reporting | Official Module + Provider | M5+ | Mapped |
| 接口限流 | Hosting | Core | M1 | Mapped |
| Elasticsearch 日志 | Elasticsearch Observability | Provider | M5+ | Mapped |
| OAuth 2.0 外部登录 | Identity OAuth Providers | Provider | M5+ | Mapped |
| APIJSON 零代码查询 | APIJSON Compatibility | Compatibility | M5+ | Mapped |
| 数据库视图与实体维护 | DatabaseTools + CodeGeneration | Official Module | M5+ | Mapped |

## 3. Core 中额外发现的能力

| Admin.NET.Pro 能力 | Full.NET 归属 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| API Key 认证 | Identity | Core | M2 | Mapped |
| 请求签名认证 | Identity Signature Auth | Official Module | M5+ | Mapped |
| 缓存管理 | Caching Admin | Official Module | M5+ | Mapped |
| 列显示个性化 | Settings + Client Preferences | Core | M3 | Mapped |
| 数据库管理 | DatabaseTools | Official Module | M5+ | Mapped |
| 枚举、常量查询 | Settings Metadata | Core | M3 | Mapped |
| 消息中心 | Notifications | Core | M3 | Mapped |
| MQTT | MQTT Provider | Provider | M5+ | Mapped |
| 开放接口访问 | OpenAccess | Official Module | M5+ | Mapped |
| 插件管理 | Modularity Admin | Official Module | M5+ | Mapped |
| 打印 | Printing | Official Module + Client | M5+ | Mapped |
| 行政区域 | Regions | Official Module | M5+ | Mapped |
| 报表配置 | Reporting | Official Module | M5+ | Mapped |
| 流水号规则 | SerialNumbers | Official Module | M5+ | Mapped |
| 系统升级 | Upgrade Management | Official Module | M5+ | Mapped |
| 支付宝 | Payments.Alipay | Provider | M5+ | Mapped |
| 微信生态 | WeChat | Official Module + Provider | M5+ | Mapped |
| RabbitMQ 事件集成 | EventBus.RabbitMQ | Provider | M5+ | Mapped |
| 国密 SM2/SM3/SM4 | Cryptography.GM | Provider | M5+ | Mapped |
| 数据导入导出工具 | ImportExport | Official Module | M5+ | Mapped |
| 服务器硬件与运行时信息 | Observability Admin | Official Module | M5+ | Mapped |
| System.Text.Json 源生成与序列化基准 | Serialization | Core | M0-M1 | Designing |
| MessagePack 可靠事件载荷 | Messaging + Outbox | Core | M1 | Designing |
| gRPC/Protobuf 跨进程同步通信 | ServiceCommunication.Grpc | Provider/Template | 首次服务拆分时 | Mapped |
| SignalR、MessagePack Hub 和 Redis Backplane | Realtime | Core + Provider | M2 | Mapped |
| 模型供应商中立 AI 抽象 | AI.Abstractions | Official Module | M5+ | Mapped |
| Agent、MCP 与 Agentic Web | Agents + AgenticWeb | Official Module + Protocol Adapter | M5+ | Mapped |

## 4. 插件能力基线

| Admin.NET.Pro 插件 | 关键能力 | Full.NET 归属 | 形态 | 状态 |
|---|---|---|---|---|
| `Admin.NET.Plugin.Ai` | AI 模型配置、对话、Agent、工具调用、MCP 与 Agentic Web | AI + Agents + AgenticWeb | Official Module + Provider + Protocol Adapter | Mapped |
| `Admin.NET.Plugin.DataApproval` | 数据变更审批 | DataApproval | Official Module | Mapped |
| `Admin.NET.Plugin.DingTalk` | 钉钉组织、消息和接口 | DingTalk | Provider | Mapped |
| `Admin.NET.Plugin.Document` | 文档、分类、标签、权限、分享、预览、版本、回收站和统计 | Document | Official Module | Mapped |
| `Admin.NET.Plugin.GoView` | 可视化大屏 | GoView | Official Module + Client | Mapped |
| `Admin.NET.Plugin.K3Cloud` | 金蝶云星空接口集成 | K3Cloud | Provider + Sample | Mapped |
| `Admin.NET.Plugin.PaddleOCR` | OCR 识别 | OCR | Provider | Mapped |
| `Admin.NET.Plugin.ReZero` | 线上建表、动态接口、授权和超级 API | DynamicApi | Compatibility + Official Module | Mapped |
| `Admin.NET.Plugin.WorkFlow` | 流程设计、发布、实例、审批、待办、抄送和业务联动 | Workflow | Official Module | Mapped |
| `Admin.NET.Plugin.WorkWeixin` | 企业微信接口集成 | WorkWeixin | Provider | Mapped |

插件的详细功能必须在各自实施前建立独立设计规格。核心模块不得为了插件反向增加业务耦合。

AI 对标不止复制模型配置和聊天页面。Full.NET 的验收范围还包括 `Microsoft.Extensions.AI` 供应商中立抽象、模型/Token/费用配额、显式 Tool 权限、Agent 会话与步骤、人工审批、MCP Client/Server、AG-UI 或等价标准 Web 协议、租户隔离和可靠审计。预览协议包必须封装在独立适配器中，不能成为核心稳定 API。

Realtime 对标分两阶段：M2 先交付 `IRealtimePublisher`、SignalR、MessagePack Hub Protocol、连接鉴权、租户分组和 Redis Backplane；M3 的 Notifications 再消费该抽象实现公告、站内信、未读数和多渠道通知。业务模块不得直接持有 `IHubContext`。

## 5. 客户端与交付形态

| Admin.NET.Pro 资产 | Full.NET 对标 | 形态 | 计划 | 状态 |
|---|---|---|---|---|
| `Web` Vue3 管理端 | `ui/admin` | Client | M2-M4 | Mapped |
| `App` 移动端 | Full.NET Mobile Client | Client | M5+ | Mapped |
| `Web_Desktop` | 桌面布局与打包能力 | Client | M5+ | Mapped |
| `Web_Artd` | 替代主题/组件适配能力 | Client | M5+ | Mapped |
| `GoView` | 可视化大屏客户端 | Client | M5+ | Mapped |

客户端对标重点是功能入口、权限、租户、状态反馈和关键流程。视觉样式可以重新设计，不要求像素级复制。

## 6. 验收规则

每一项从 `Implemented` 进入 `Verified` 前必须满足：

1. 已明确 Core、Module、Provider、Compatibility、Sample 或 Client 归属；
2. 后端权限、租户隔离和审计规则已覆盖；
3. 关键业务流程与 Admin.NET.Pro 基线逐项比较；
4. 差异是有意设计并记录，不是遗漏；
5. SQL Server 和 MySQL 的适用测试通过；
6. API 契约、前端交互和错误处理通过测试；
7. 来源、许可证和直接复用情况已经登记；
8. 文档和升级说明已经完成。

## 7. 维护规则

- Admin.NET.Pro 基线升级或本地授权版本新增功能时，必须更新本矩阵；
- 每个 Full.NET 里程碑结束时复核一次矩阵状态；
- 新功能默认先判断是否为 Core，不能因为对标要求而直接放入核心；
- `Not Applicable` 必须经设计评审，并给出替代能力或不实现的技术理由；
- 功能对标不能突破 MIT 发布和第三方授权边界。
