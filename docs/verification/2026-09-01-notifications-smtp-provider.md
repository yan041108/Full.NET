# Notifications SMTP Provider 验证（2026-09-01）

- **任务：** Notifications 首个外部渠道 `email.smtp`
- **基线：** `e3f0d6b6776574376f9cab3117f8dc802d2ba838`（`main`）
- **快照：** `notifications-smtp-provider-20260831`
- **状态：** Adapter **Build-verified**；QQ SMTP **External-auth-not-verified**；容量 **Capacity-not-verified**

## 交付范围

- 生产模块内新增 MailKit 4.17.0 SMTP Adapter，固定 `ProviderTypeKey=email.smtp`、`ChannelKey=email`、`EndpointKindKey=email` 与 `ReceiptModeKey=none`。
- 非密钥配置闭合为 `host`、`port`、`secureSocketMode`、`username`、`fromAddress`、`fromDisplayName`；只允许 `ssl_on_connect` 或 `starttls`，未知字段、非法主机/端口/地址失败关闭。
- 密码只通过 `env://<NAME>` 在 Worker 投递瞬间读取；没有把用户提供的账号或授权码写入仓库、Profile JSON、测试快照、日志或异常消息。
- Worker 只解密与当前 `TenantScopeKey + UserId + ProviderProfileVersionId + email` 精确匹配且状态为 `verified` 的端点；缺失、待验证、错误 Profile 或解密失败时不调用 Provider，也不回退到用户 GUID。
- SMTP 只在 `Notifications:Providers:Smtp:Enabled=true` 时进入 API/Worker 共用的闭合 Provider 目录；默认关闭。当前切片只发送纯文本，不实现 HTML、附件、抄送/密送、连接池、DSN 或送达/已读回执。
- MailKit/MimeKit 的 MIT 许可已进入 `THIRD-PARTY-NOTICES`；未启用协议日志，未绕过服务器证书验证。

## 自动化证据

| 命令/范围 | 结果 |
|---|---|
| SMTP RED：聚焦 Unit 在类型尚未实现时编译 | **RED**：缺少 `Providers.Smtp`、Adapter、Resolver 与 Transport 类型 |
| SMTP 聚焦 Unit + 模块条件注册 + Provider 目录 | **21/21** |
| Notifications 全部 Unit | **65/65** |
| SQL Server `NotificationsApiSqlServerTests` | **1/1** |
| MySQL `NotificationsApiMySqlTests` | **1/1** |
| `pnpm test:slice -- --snapshot notifications-smtp-provider-20260831` | **2/2**，SQL Server/MySQL 各一组；Release 构建 0 警告、0 错误 |
| `NotificationsPlatformBoundaryTests` | **5/5** |
| `NativeAotStaticBindingRulesTests` | **46/46** |
| `pnpm test:aot:analyzers` | Host.Api 分析构建通过，0 警告、0 错误 |
| `pnpm test:aot:worker:analyzers` | Worker 分析构建与 JIT Rebuild 通过，0 警告、0 错误 |
| `pnpm test:naming` | **30/30** |
| `pnpm test:openapi` | **122/122** |
| `pnpm test:governance` | **52/52** |

聚焦 Architecture 组合运行另发现当前分支既有的 SerialNumbers 动态 `SqlStatement` 构造，以及 Identity/Tenancy Dapper 扫描、Kafka 旧装配名断言；Notifications 专属边界与 Native AOT 静态绑定聚焦组均全绿，未把完整 Architecture 描述为通过。

## QQ SMTP 外部实测

- 运行时参数使用 `smtp.qq.com:465`、`SslOnConnect`、完整 QQ 邮箱用户名和用户提供的 16 位 SMTP 授权码；收件地址与发件账号相同。
- 参数经交互式 PowerShell `SecureString` 注入一次性子进程环境；进程退出后清除环境变量并释放 BSTR。测试输出只保留稳定类别和阶段，没有输出账号、授权码或 SMTP transcript。
- TLS 连接成功，服务器公布 `LOGIN`、`PLAIN`、`XOAUTH`、`XOAUTH2`。MailKit 自动认证在 `Authenticate` 阶段收到协议中断；随后仅用于定位的显式 `LOGIN` 仍返回 `AuthenticationException`。
- 结论：服务器没有接受当前认证信息，未进入发送阶段，没有证据表明测试邮件已被 SMTP 服务器接受。应在 QQ 邮箱侧确认 SMTP 服务已开启并重新生成授权码后再执行同一外部测试；本次失败不能归因于 465/SSL 连接代码。
- 调试同时关闭了一个分类缺陷：认证阶段的 `SmtpProtocolException` 现在按永久认证失败处理，避免错误地作为瞬时网络故障反复重试；连接或发送阶段的协议异常仍为瞬时失败。

## 完成边界

- `Accepted` 只代表 SMTP 服务器接受消息，不代表收件、送达或已读；本次外部实测连 `Accepted` 也未达到。
- 未执行 Linux Native AOT publish/原生进程 SMTP E2E，不标记 `Aot-published` 或 `Native-provider-verified`。
- 现有 `RecipientEndpointStore` 仍未暴露自助/API/UI 管理入口；真实业务接入前必须补齐受验证邮箱端点登记流程与精确权限。
- 未执行负载、限速、多租户账号矩阵、连接复用、退信对账或生产密钥托管认证，不外推为生产就绪。

## 参考

- 腾讯云 QQ 邮箱配置说明：`smtp.qq.com`，465 使用 SSL，587 使用 STARTTLS，密码使用 SMTP 授权码。
- MailKit 官方文档：465 对应 `SecureSocketOptions.SslOnConnect`；本切片固定 4.17.0，并保留证书验证。

本任务未触发规则或 Skill 演进。
