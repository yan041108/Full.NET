# 执行清单 C 区（48–82）源码审计（2026-09-23）

**目的**：Phase C 严格单编号串行；下列「已有」指 Host.Api + `ui/admin` 可见实现，**不等同 Verified**。Gate C（WF+DA parity）仍为 **Build-verified** 时启动 C 区仅作**证据槽**，不隐式升级 Gate C。

| 编号 | 判定 | 主要锚点 |
|------|------|----------|
| 48 | **closeout** | `ManageRegistrationPolicy` / `ManageRegistrationWays`；`RegisterAccount` + 邮箱挑战；`PublicRegistrationWays`；`RegistrationWaysView` + `RegisterView`；**无**短信登录产品入口 |
| 49 | **closeout** | `ManageLdapConnections`；`LdapBindPasswordProtector`；`LdapDnScopeValidator`；`preview-sync` / `test-authentication`；`LdapConnectionsView`；**无**有审计的同步应用 |
| 50 | **closeout** | `ManageOAuthProviders` / `OAuthFlowService`（PKCE/state/nonce）；`oauth/callback`；`LoginView` + `OAuthCallbackView`；`SecuritySettingsView` 绑定/解绑；**无**未验证邮箱自动合并 |
| 51 | **closeout** | `OssHostFileBlobStorage` + `FileStorageProviderRegistry`；`ManageStorageProviders`；`StorageProvidersView`；**无** COS、**无**批量迁移 |
| 52 | **closeout** | `BrowseHostCatalog` / `CodeGenerationCatalogView`；`migration-draft` + `CatalogMigrationDraftGenerator`；HostOnly 元数据 SQL；**无**在线 DDL / 任意 SQL |
| 53 | **closeout** | `QueryHostModuleSelection` / `ModuleSelectionPreviewView`；`FullNetModuleSelection` DAG；`ModuleSelectionEndpointTests`；**无**运行时动态加载 |
| 54 | **closeout** | `ManageBackupExecutor` / `BackupExecutorView`；`BackupRunArtifactService` 受控下载；**无**生产恢复 API |
| 55 | **closeout** | `ElasticsearchSerilogSinkConfigurator`；`MonitorElasticsearchLogPipeline`；`ObservabilityElasticsearchHealthView`；**无**业务模块 ES 耦合 / OTel Logs 重复 |
| 56 | **closeout** | `Full.NET.Modules.Mqtt` 控制面；`MqttTopicAccessPolicy` / 速率 / 幂等；`MqttControlPlaneView`；**非** Kafka 对外 Provider |
| 57 | **closeout** | `Full.NET.Modules.Cryptography` / `GmSm2SignatureEngine`；`CryptographyGmKeysView`；仅 SM2 sign/verify；**无**通用 decrypt |
| 58 | **closeout** | `DingTalkNotificationProviderAdapter` / `DingTalkAccessTokenCache`；`im.dingtalk` + 互动卡片；`NotificationProviderProfilesView` / `NotificationDeliveriesView` / `NotificationPreferencesView`；**无**审批镜像（59） |
| 59 | **closeout** | `DingTalkApprovalSyncService` / `DingTalkApprovalSyncHostedProcessor`；`approval-sync` API + 验签回调；`DingTalkApprovalSyncView`；Workflow 权威、**无**钉钉驱动本地审批 |
| 60 | **closeout** | `WeComNotificationProviderAdapter` / `WeComAccessTokenCache`；`im.wecom` 文本应用消息；通知 Profile/投递/偏好；**无**通讯录同步、**无**群聊/富媒体 |
| 61 | **closeout** | `WeChatMiniProgramNotificationProviderAdapter`；`ManageWeChatMiniProgramBindings`；`WeChatMiniProgramBindingsView`；订阅消息 + OpenId 掩码；**无**公众号客服/素材/二维码 |
| 62 | **closeout** | `DocumentVersionRetention*` / `DocumentVersionDeletionService`；`version-retention` API；`DocumentStatisticsView` + `HostDocumentItemsView` 删除；依赖 **47** 回滚 |
| 63 | **closeout** | `HostDocumentStatisticsQueryService` / `HostDocumentAccessLogQueryService`；`DocumentStatisticsView` 统计+访问日志页签；**无**热门标签/批量分享 |
| 64 | **closeout** | `DocumentOfficePreviewConversion*` / `DocumentPreviewTaskRunner`；`DocumentPreviewTasksView`；Provider `external_http`/`external_process`/`disabled`；**无** Tenant 文档 |
| 65 | **closeout** | `StaticImportSchemaRegistry` / `ImportExportTaskManagementService` 预校验；`BrowseStaticSchemas` + 任务 API；`ImportExportTasksView`；消费者 `organization.tenant_positions` |
| 66 | **closeout** | `ImportExportTaskRunner` / `ImportExportTaskExecutionService`；`execute`/`resume`/`retry`/`error-receipt`；`ImportExportTasksView` 断点 UI；`ImportExportTaskClaimPersistenceAssertions` |
| 67 | **closeout** | `ReportingDataSourceSecretProtector`；`ReportingDataSourceFieldValidator`；列表脱敏 + `test` API；`ReportingDataSourcesView`；`sql_server`/`mysql` 白名单 |
| 68 | **closeout** | `ReportingQueryPortCatalog`；groups/definitions CRUD + `publish`/`versions`；`ReportingDefinitionsView`；`ReportingParameterSchemaValidator` |
| 69 | **closeout** | `ReportingDefinitionExecutionService`；`ReportingExecutionPolicy`；`execute` 分页 API；`ReportingExecuteView`；列权限与失败即停 |
| 70 | **closeout** | `ReportingExportTaskRunner` / Excel 导出；`ReportingExportPolicy`；`export-tasks` + download；`ReportingExportTasksView`；租户上下文 |
| 71 | **closeout** | `PrintingFormSchemaCatalog`；模板 publish/versions；`PrintingFormBindingService` + preview API；`PrintingPreviewView`（DOMPurify/浏览器打印） |
| 72 | **closeout** | `AiModelConfig*` + `AiApiKeyProtector`；`test` 连通性；`tenant-quotas`；`AiModelConfigsView`；`AiProviderKeys` 白名单 |
| 73 | **closeout** | `AiChatSession*` + `AiChatStreamService`；SSE `messages/stream` + `cancel`；`AiChatView`；`AiChatScope`；配额预留 |
| 74 | **closeout** | `AiAgentToolCatalog` + `agent-tool-calls` 审计；`McpExposurePolicy`；`AiAgentToolsView`；只读静态目录 |
| 75 | **closeout** | `PaymentMerchantConfig*` + `PaymentOrder*`；`wechat_native` 首切片；`PaymentMerchantConfigsView` / `PaymentOrdersView`；凭据保护 |
| 76 | **closeout** | `ReceiveWeChatNotify` + `PaymentWeChatNotifyService`；`reconcile` / `PaymentRefund*`；`PaymentOrdersView` 对账退款 + `PaymentRefundsView`；`PaymentNotifyBindingTests` |
| 77 | **closeout** | `AlipayPagePayClient` + `AlipaySigner`；`alipay_page` 商户/订单 UI；trade.page.pay + trade.query；**无** notify/转账 |
| 78 | **closeout** | `GoViewProject*` HostOnly；画布校验 + publish 版本；`preview` 只读快照；`GoViewProjects/Editor/PreviewView` |
| 79 | **closeout** | `K3CloudWebApiClient` ValidateUser + Save/Submit；`SAL_SaleOrder` 切片；`K3CloudConnection*` / `DocumentSync*` 视图；无万能 RPC |
| 80 | **closeout** | `PaddleOcrIdCardClient`；Host Files 源图；`confirm`/`reject`；`OcrIdCardMasking`；Provider + 任务视图；不写 Identity 权威 |
| 81 | **closeout** | `clients/uniapp` 微信切片；workflow 待办/审批 + notifications 站内信；`mp-weixin-identity-session`；H5 `phase-c-81` mock E2E |
| 82 | **closeout** | `clients/flutter` 工作流待办切片；`IdentitySession` + `WorkflowTodoClient`；`contract.test.mjs`；无完整后台复制 |

**停止边界（48）**：短信登录独立后续切片；依赖 45 的验证码能力不默认开放公网注册；默认 `IdentityRegistrationMode.Disabled`。

**停止边界（49）**：确认映射后的目录同步应用另拆编号；预览 DN 必须在 `SyncSearchBaseDn` 子树内。

**停止边界（50）**：OAuth 登录必须先绑定；多 Provider 扩展不超出本槽首个 Provider 配置模型。

**停止边界（51）**：COS 另编号；历史 `ProviderKey` 仅读取/补偿，不在本槽批量改存。

**停止边界（52）**：列 UI 同步见 B-36；ReZero/动态 API 不在 52 实现。

**停止边界（53）**：配置变更仅预览/校验；生效须部署重启；Plugin 热加载不在范围。

**停止边界（54）**：备份执行器与凭据/存储由部署侧配置；恢复操作另立流程。

**停止边界（55）**：仅 Serilog→ES 适配与健康观测；全量日志检索 UI 不在本槽。

**停止边界（56）**：首个受控发布 + 记录查询；设备海量订阅/桥接另拆编号。

**停止边界（57）**：integration 载荷签验；SM4/证书全生命周期另编号。

**停止边界（58）**：仅互动卡片 + 平台 Profile/投递/偏好；钉钉审批同步与组织权威见 **59** 及 Identity/Organization。

**停止边界（59）**：固定首种 processCode 镜像；多模板/双向流程权威另编号；58 卡片通道不替代本槽。

**停止边界（60）**：仅文本应用消息 + 平台配置页；企微组织/标签/群聊与其余消息类型另编号。

**停止边界（61）**：小程序 js_code 绑定与订阅登记首切片；公众号与其它微信能力另编号；收件端点不经偏好页手填 OpenId。

**停止边界（62）**：保留策略 + 授权删历史版本；当前版本/最小保留数不可绕过；47 回滚不重复验收。

**停止边界（63）**：首批汇总指标 + 访问日志页签；DC03 其余产品面另拆；与 Auditing 访问日志分流。

**停止边界（64）**：Host Office→PDF 任务队列；真实转换器/字体许可由部署 Provider 验收；Tenant 文档另立范围。

**停止边界（65）**：静态 Schema 模板 + 预校验入队；执行/错误回执/断点 UI 见 **66**；UI 新建向导未启用（API 路径为准）。

**停止边界（66）**：分批执行 + 错误回执下载 + resume/retry；不覆盖 Reporting（**67**）；partial 场景 UI 手测另需失败行夹具。

**停止边界（67）**：数据源 CRUD + 脱敏列表 + 连接测试；定义/Query Port/执行/导出见 **68+**；不接受任意连接串。

**停止边界（68）**：静态 Query Port + 定义草稿/发布版本；有界执行与结果页见 **69**；不暴露审查 SQL 给客户端。

**停止边界（69）**：参数化 execute + 分页结果；导出任务见 **70**；只读库凭据不替代 Endpoint 授权。

**停止边界（70）**：Excel 异步/同步导出任务 + 受控下载；PDF/HTML 与 Printing **71+** 另编号。

**停止边界（71）**：固定表单 `tenant_profile_card` + 模板版本 + 浏览器预览/打印；硬件打印与 AI **72+** 另编号。

**停止边界（72）**：模型配置 + 连通性测试 + 租户配额；聊天流式 **73**；Agent/MCP 另编号。

**停止边界（73）**：单用户聊天 SSE + 历史 + 取消；Agent 工具审计 **74**；写工具人审另拆。

**停止边界（74）**：静态 Tool 目录 + 调用审计；MCP 连接配置/Agent Run **另编号**；Payments **75**。

**停止边界（75）**：商户配置 + 订单创建/查询（微信 Native 首选）；回调/退款/对账 **76**；禁止真实扣款测试。

**停止边界（76）**：notify 幂等与绑定、对账、单笔退款与退款列表；无 signed live 回调 E2E；支付宝页渠道 **77**。

**停止边界（77）**：`alipay_page` 下单 URL + 对账查询；无支付宝 notify、无转账；GoView **78**。

**停止边界（78）**：JSON 画布草稿 + 发布版本 + 只读预览；无独立 GoView OSS 客户端、无预览旁路 SQL；K3Cloud **79**。

**停止边界（79）**：销售订单 Save/Submit + 同步状态页 + 连接测试；无 Audit、无第二单据、无 live 金蝶 E2E；OCR **80**。

**停止边界（80）**：身份证 Paddle Provider + 受控上传 + 人工确认；无通用 OCR、无自动档案合并；uni-app **81**。

**停止边界（81）**：微信小程序 + H5 代理验收；待办/站内信权限与 API 消费；无支付宝小程序发布、无完整移动后台；Flutter **82**。

**停止边界（82）**：Flutter 工作流待办首切片；契约测试 + 内存会话；无 Flutter CI/商店发布；**Phase C 48–82 证据串行结束**。
