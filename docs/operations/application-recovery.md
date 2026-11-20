# 应用恢复

> 本文规定联合恢复的操作门禁，不表示备份、恢复演练或 RPO/RTO 已通过。先在隔离环境演练并按实际部署填写恢复清单；备份执行器的成功记录不等于可恢复证明。

## Ubuntu 基础设施备份的生成与完整性

`eng/deploy/ubuntu/Backup-Infrastructure.sh` 用于现有 Ubuntu/MySQL/Redis/单节点对象存储参考部署，只生成新目录，不恢复数据库、不覆盖历史备份。配套的 `Verify-InfrastructureBackup.py`、`fullnet-backup.service` 和 `fullnet-backup.timer` 必须一起部署；生成器在签发 COMPLETE 前调用同目录下的校验器，缺少校验器会使本轮备份失败。

安装脚本到 `/usr/local/lib/fullnet/` 时使用 root 属主和 `0700`，服务与定时器安装到 `/etc/systemd/system/`，使用 `0644` 并重载 systemd。服务限制三十分钟和 512 MiB；定时器以 UTC 每日 19:00 加随机延迟执行。此处是部署说明，本项目的工具测试不会在目标服务器启用定时器。

生成器由受控的 `/etc/fullnet/infrastructure-installed` 读取 primary/secondary，按角色生成必需归档、ROLE 与 SHA256SUMS。primary 的 MySQL 快照位点来自 `mysqldump --source-data=2 --single-transaction` 写入的转储注释，不使用转储结束后查询的当前位置。该方式的一致性范围为事务表，期间仍须禁止相冲突的 DDL，见 [MySQL 8.0 官方转储说明](https://dev.mysql.com/doc/refman/8.0/en/mysqldump.html)。

secondary 即使对象服务原本已关闭也要备份磁盘数据；原本运行时短暂停服取得归档，再恢复原运行状态，原本关闭时保持关闭。服务处于启动/停止过渡状态、数据目录缺失、归档或恢复运行状态失败时，不生成 COMPLETE。应用侧须按实际部署安排这一单节点存储的短暂停服窗口。

独立完整性核对不需要启动数据库或服务：

```bash
python3 /usr/local/lib/fullnet/Verify-InfrastructureBackup.py "$BACKUP_DIRECTORY" --role primary
```

secondary 使用 `--role secondary`。校验器要求私有目录和载荷权限、必需文件、K3s 数据/密钥成对、精确且无重复或越界路径的摘要清单以及全部 SHA-256 相符；链接和未知文件会被拒绝。`--staging` 仅供生成器检查尚未完成的输出，返回 state=staged，不能作为完整备份放行。

COMPLETE 只证明本轮归档流程和完整性检查成功，不证明数据库、文件和密钥处于同一个恢复点。Data Protection、K3s 等条件归档按现场安装状态生成，是否满足选定应用仍须在恢复前复核。RESTORE-VERIFIED 等后续标记不会使校验器返回恢复已验证；实际隔离恢复、撤权与外部回执对账及 RPO/RTO 仍按下文执行。旧格式备份须保留原证据单独核对，不通过补写 ROLE 或标记冒充新格式验收。

## 对象存储隔离恢复入口

在已安装对应运维脚本的 secondary 节点，以 root 显式选择一个完整备份：

```bash
sudo /usr/local/lib/fullnet/Verify-ObjectRestore.sh 192.0.2.20 /var/backups/fullnet/BACKUP_ID
```

第一个参数为恢复服务所用、与原对象证书匹配的 IPv4；第二个参数必须是 /var/backups/fullnet/ 内的明确目录，不再自动选“最新”备份。先按 secondary 角色校验 ROLE、COMPLETE、权限与精确摘要，再将对象归档复制到本轮临时目录并复核。归档仅允许 srv/fullnet/object-storage/ 下普通文件和目录，拒绝越界、规范路径重复、链接、设备、FIFO、父文件冲突、空数据及空间不足；最多十万成员。不会恢复或初始化在线数据库。

恢复与备份生成共用读写互斥锁，对象恢复另有单实例锁。SeaweedFS 只在本轮独立数据目录、PrivateNetwork=yes 的 transient unit 中运行；先复核 MainPID、PrivateNetwork、WorkingDirectory，再为独立网络配置证书目标地址。协议探针使用隔离 Python 模式，检查原持久化值、版本控制与鉴权，忽略 PYTHONOPTIMIZE 对断言的影响；不会对在线对象服务执行 prepare。检查最多四十五次、总预算一百八十秒，服务有三百秒运行上限。

通过后再核对原备份完整性，随后停止本轮 unit、复核 MainPID=0 并删除临时数据，最后才排他创建私有 OBJECT-RESTORE-VERIFIED JSON 回执，包含原清单/归档摘要和完成时间。停止、进程退出、目录清理或回执提交任一失败均不签发本轮成功；旧标记一律保留并拒绝覆盖，重新演练前须将旧证据归档到备份目录以外，不以旧日期标记代表本次运行。启动响应丢失、TERM/INT 和失败退出也会尝试清理本轮 unit。

失败原始诊断仅保留 /var/log/fullnet/fullnet-object-restore-*.log，私有且最多 64 KiB，不回显认证错误正文。诊断保存失败仍继续停止实例；若停止或删除失败，保留相关运行目录并明确报告 unit 名，人工核对该 unit 的 MainPID 与 WorkingDirectory 后再处理，不能直接删除正在使用的数据。强杀、主机掉电与本入口之外的 systemd 状态仍需人工复核。

定向入口 helm-runtime-focused / object-restore 在一次性禁网容器中执行真实 Bash、归档/摘要/锁/文件权限与清理，systemd、网络命名空间和 SeaweedFS 为受控替身，不代表目标实际恢复、真实 S3、RPO/RTO 或容量已通过。完整应用仍须按下文恢复数据库、密钥、授权和外部回执。

## 场景

| 场景 | 首要动作 |
| --- | --- |
| 数据库损坏或整站恢复 | 隔离恢复环境和外发，冻结并核对数据库、文件、密钥及运行状态的恢复点 |
| Api 不可用 | 检查 Api Pod/进程和依赖；涉及数据回退或授权不确定时先停止相关 Worker 和外发 |
| Worker 积压 | 核对 Outbox/Jobs、失败原因及下游容量；不因积压直接重放未知外部动作 |
| 秘密泄露 | 按事件响应流程隔离并轮换受影响秘密；禁止把已撤销密钥当作有效凭据重新启用 |

## 恢复前冻结

- 记录事件时间、停止准入和各组件停止时间、目标环境、负责人、批准的恢复点及 RPO/RTO；保留故障现场和恢复前证据。不要在未授权时覆盖生产数据库、对象文件或密钥。
- 停止或隔离 API 写入、Worker、定时任务、Outbox 发布者及 CDC/Kafka Relay，并隔离支付、邮件、Webhook、模型等外部出口与回调。确认旧实例和其他集群不再同时处理同一事件流；单纯关闭入口不代表后台副作用停止。
- 固定已选预设、应用和框架版本、镜像/分发包摘要、配置与迁移清单；分别登记数据库备份及日志、文件快照、Data Protection Key Ring 和保护证书、OIDC 签名密钥及其版本、消息位点与外部回执。秘密材料只存受控秘密系统，清单不记录明文。
- 数据库与文件快照无法原子取得时记录各自时间窗，并对照 [Files 本地存储备份与恢复](./files-local-storage.md#备份与恢复) 保留差异清单。缺失对象、不可解密材料或恢复点之后的撤权与外部交易未核清时保持隔离，不以最新时间戳猜测一致。

## 联合恢复顺序

1. 在独立隔离环境恢复选定数据库备份及可用日志，确认实际恢复点、完整性、数据库提供程序与迁移记录。按已冻结版本运行 Migrator 并核验结果；失败立即停止，不用手工改业务表或伪造迁移记录。
2. 恢复所选 Provider 的文件对象及元数据对应快照，核对引用、对象存在性、大小和 SHA-256，登记缺失与孤立对象。`local` Provider 要求 API 和 Worker 使用同一持久根目录；S3/OSS 按实际提供商恢复桶内对象及受控访问配置。沿原文件记录中的 Provider 读取，不自动回退到当前默认 Provider；代码支持和配置校验不代表目标云存储已经完成恢复演练。
3. 从受控秘密系统恢复匹配恢复点和当前安全策略的 Data Protection Key Ring、`ApplicationName`、活动及必要历史保护证书，以及 OIDC 签名密钥和权威配置。核对私钥可用性、密钥状态及证书轮换历史；秘密泄露或恢复点之后的吊销不能被旧备份撤销。缺失或已泄露的密钥须按事件处置及会话撤销策略处理，不能用新临时密钥假装旧数据可解密。
4. 在持续阻断外发和公共流量的前提下，核对租户活动状态、账号、角色与权限、会话和签名密钥权威状态，以及恢复点之后发生的撤销。将较新的可信撤销和外部交易记录与恢复结果对账；无法证明安全时不得恢复访问或派发。使用隔离缓存/Backplane 并验证权威读取，不能把旧缓存当作授权事实。
5. 核对 Outbox、Inbox、任务、租约、死信、支付/退款、通知、Webhook 和其他外部回执，比较 Broker/CDC 位点及保留范围。对已发送或结果未知的动作按原业务身份与外部权威记录对账；不因数据库回退或队列积压批量重发、重建幂等键、清空账本或将未知结果当作未执行。按批准配置确保每条事件流只有一个发布所有者。
6. 所有前置检查通过后，先在隔离环境启动 API 做受控健康、登录、权限、文件和关键读取验证，不开放公共流量、不产生真实外部副作用。再按业务批准逐项恢复所需 Worker、发布者和外部连接，核对处理回执及重复防护；最后开放流量并持续观察。若无法在当前部署阻断副作用，就保持组件停止，先建立并验证隔离措施。

常规发布脚本 `eng/deploy/Invoke-FullNetRelease.ps1` 会依次启动 Migrator、Worker 和 API，**不会**执行上述联合恢复、外发隔离或逐项对账门禁；不得把它直接作为灾难恢复编排器。正常升级与联合恢复的启动约束不同。

## 验证与计时

- 演练前冻结每项数据和业务的 RPO/RTO 目标、时钟来源与成功标准；记录故障发生、最后可信提交、实际恢复点、隔离开始、关键业务及外部副作用受控恢复的时间。
- 将最后可信提交与实际恢复点比较，并核对恢复点之后的业务、权限、文件和外部交易差异；时差只是 RPO 的时间观测，实际丢失范围须靠对账证据确认。
- 从约定的故障起点至关键业务及必要依赖受控恢复的时刻计算 RTO，分别保留数据库恢复、迁移、密钥/文件核对及对账耗时；健康检查或备份成功不能替代业务恢复。
- 任一对象、权限、会话、密钥、消息位点或外部结果无法核对时记录阻断与责任人，继续隔离；未经实际演练不得报告 RPO/RTO 或联合恢复已达标。

### 联合演练回执复核

将已核验的脱敏回执放入同一独立目录。下表每一项对应一个固定文件，负责人在相应检查完成后才记录 `outcome: "passed"`。包含文件恢复的联合演练应选用装配 Files 的候选，并实际校验至少一个恢复文件的引用与字节。备份的单项成功标记可作为原始证据。

| 回执文件 | 签发前实际核对 |
| --- | --- |
| `database.json` | 备份与日志恢复、数据库完整性和 Migrator 结果 |
| `files.json` | 文件元数据与对象对应关系、大小和 SHA-256 |
| `keys.json` | 恢复的保护密钥可解密，签名配置和必要证书可用 |
| `access.json` | 隔离 API 的关键读取、登录及权限复核，撤销差异已核清 |
| `dispatch.json` | 外发对账、发布者唯一性、Worker 受控处理与重复防护 |

每份回执使用 `schemaVersion: 2`，包含 `drillId`、`candidateSha256`、`check`（不带扩展名的固定文件名）、`outcome` 和 `checkedAtUtc`。数据库和文件回执另含 `recoveredThroughUtc`，记录各自可证实的恢复上界。回执中仅保存脱敏结果及受控证据的引用。

同目录的 `recovery.json` 使用相同的 `schemaVersion`、`drillId` 和 `candidateSha256`。`receipts` 按 `check` 保存各回执文件原始字节的 SHA-256。另记录 `rpoTargetSeconds`、`rtoTargetSeconds` 两个演练前冻结的整数秒目标，以及 `drillStartedAtUtc`、`faultAtUtc`、`isolatedAtUtc`、`lastTrustedCommitAtUtc`、`verifiedRecoveryPointAtUtc` 和 `businessRecoveredAtUtc`。所有时间使用 UTC `Z` 格式，支持毫秒。联合恢复点须已对账，且不晚于数据库或文件的已证实上界。

`access.json` 另须记录 `revocationsComparedThroughUtc` 和 `unresolvedRevocations`。可信撤权来源至少覆盖到故障时刻，再按原用户、租户和资源身份通过正式管理入口重放所需撤销，并验证原凭据和资源访问被拒绝。核对上界不能晚于实际检查时间，未解决数量须为 0。`dispatch.json` 的 `unresolvedExternalOutcomes` 同样须为 0。缺少可信来源、未解决项尚存或只核对到旧备份时，不签发通过回执。

账号与会话撤销对账时，按可信撤销记录中的用户 UUID，在恢复环境调用 `POST /api/v1/identity/online-sessions/users/{userId}/revoke-all`，并用原会话确认受保护入口返回 401。该操作需要 `identity.sessions.revoke`。若该账号还应保持禁用，再调用 `POST /api/v1/identity/users/{userId}/disable`，核对账号状态及新登录被拒绝。该操作需要 `identity.users.disable`。保留接口回执及拒绝结果，处理失败时继续隔离。

Worker 恢复前还应核对这些账号的在途任务。对原会话已失效的排队导出，验证 Worker 以 `reporting.export_authority_denied` 收口，且未生成新文件。原来已经完成的任务及文件继续保留，下载权限按当前账号、会话和资源授权复核。

应用 JWT 的签名配置使用 `Identity:ActiveKeyId` 和 `Identity:SigningKeys`。恢复时，从当前可信秘密版本核对活动私钥和全部允许验签的公钥。已撤销的旧密钥须从验签集合中移除，并使所有签发节点、API 和 Worker 使用该配置。仅切换 `ActiveKeyId` 时，仍保留在 `SigningKeys` 中的旧公钥继续参与验签。

在隔离环境逐个验证已撤销 `kid` 的旧令牌返回 401，新登录签发预期的新 `kid`，新令牌能通过当前权限校验。保持原 Data Protection 材料，并通过依赖受保护配置的实际操作核验解密能力。OIDC 的 `Identity:Oidc:SigningKeys`、活动策略、退役记录及 JWKS 有各自治理流程，恢复时另行对照当前可信状态核验。

OIDC 恢复时，先核对可信记录中的活动 `kid`、各公钥摘要及已退役项，再读取恢复环境的 `/api/v1/identity/oidc-signing-keys` 和 `/.well-known/jwks`。数据库备份可能同时回退活动策略和退役记录，单独恢复秘密配置不足以确认治理状态。按当前可信记录通过 `/{keyId}/activate`、`/{keyId}/retire` 重放合法变更，再重启并复核活动策略与 JWKS。管理路径均相对于 `/api/v1/identity/oidc-signing-keys`。

激活继续满足预发布时间、签发私钥及节点就绪要求。实际调用签发节点的 `/health/ready`，确认签名材料及策略校验通过、就绪租约已更新。曾使用的旧密钥继续满足保留窗口，窗口内退役被拒绝时保持相应公开材料。确认退役后，检查该 `kid` 已从 JWKS 移除、重新发布被拒绝，并核对配置中的旧材料未在重启后重新启用。缺少保留期公钥、材料摘要不符或可信治理记录不完整时继续隔离。

```powershell
node eng/deploy/Verify-FullNetRecoveryEvidence.mjs $EvidenceDirectory $TrustedCandidateSha256
```

候选摘要取自已冻结的发布记录。工具核对回执文件摘要、演练身份和事件顺序，报告以毫秒计的 RPO/RTO。回执缺失、校验失败或任一目标未达到时退出 1。结果仅用于复核已提交的回执。恢复执行、原始证据核验和放行仍由恢复负责人完成。

### 通知队列外发隔离

确认旧 Worker 已停止后，在恢复环境所有通知 Worker 上设置 `Notifications__Worker__Enabled=false` 再启动。Helm 部署在恢复环境的 values 中设置 `worker.notificationDeliveryEnabled: false`。Worker 启动时读取该值并记录 `Notification delivery is disabled by Worker configuration.`。通知入队仍可继续，暂停期间投递循环不会领取记录、调用渠道或采样该 Worker 的积压指标。DingTalk 审批同步、API 内的同步验证码或收件端点验证使用各自的入口，恢复时继续隔离相应外部出口与 API 写入。

在 `GET /api/v1/notifications/deliveries/{deliveryId}` 核对原投递和尝试记录，按业务身份与渠道证据确认已发送、未调用和未知结果。对 `unknown` 或 `dead_lettered` 且没有有效租约的记录，有 `notifications.deliveries.reconcile` 权限的操作员可调用 `POST /api/v1/notifications/deliveries/{deliveryId}/reconcile`，提交当前 `revision`、已核实的 `outcomeKey`（`confirmed_accepted` 或 `confirmed_not_called`）以及简短的 `evidenceReference`。若归属、渠道结果或版本仍有差异，保持隔离。恢复库中仍为待发送但外部结果未知的记录不符合当前对账入口的状态条件，须继续保留待处置。

如果恢复库的 SMTP 邮件仍为 `accepted`，且邮件中继的权威记录证实原邮件已经受理，保持外发暂停并提交同一对账入口。`outcomeKey` 填 `confirmed_smtp_accepted`，`revision` 填恢复库当前版本，`evidenceReference` 填中继记录的原始 Message-ID，去掉外围尖括号。保存可核验的中继记录和恢复操作关联。服务验证租户归属，并按原意图、收件人及 SMTP Profile 版本重算 Message-ID。证据、版本、状态或租约冲突返回 409。确认后原投递进入 `sent`，记录对账审计，尝试历史保持原样。缺少中继受理证据时继续隔离该记录。

完成通知对账及放行复核后，恢复对应配置为 `true`，重启 Worker 并观察原队列接续。配置缺省为启用。

### 支付和退款对账

保持恢复环境的支付写入和资金履约消费隔离，按原商户、租户、商户订单号和商户退款单号核对渠道权威记录。操作员用 `GET /api/v1/payments/orders/{orderId}` 读取原订单，再以 `payments.orders.reconcile` 权限调用 `POST /api/v1/payments/orders/{orderId}/reconcile` 查询渠道并应用一致的结果。

订单处于 `refunding` 时，对账要求唯一的原退款意图，并核对退款归属、原商户订单号、币种、金额和已登记的渠道退款标识。若渠道订单摘要显示已退款但恢复库缺少原退款链路，保留原订单和渠道证据，将差异交给资金负责人。对账未收敛期间保持隔离，避免通过新建订单或退款补齐丢失的记录。

### Webhook 未确认投递对账

保持 Webhook 外发隔离。确认旧 Worker 已停止领取和发送后，可用 `Webhooks__Delivery__Enabled=false` 启动恢复环境的所有 Worker，暂停 Webhook 领取及外发，其他后台处理可按恢复步骤受控启动。该值在宿主启动时读取。检查 Worker 日志中的 `Webhook delivery is disabled by Worker configuration.`，并核对原投递状态和尝试数量。恢复 Webhook 时，先完成投递处置和放行复核，再以 `Webhooks__Delivery__Enabled=true` 重启对应 Worker。

核对租户订阅的投递和尝试记录，并从接收方的权威业务回执取得同一故障范围的证据。两个文件均使用 JSON 数组，每项包含 `tenantId`、`subscriptionId`、`eventId`、`deliveryId` 以及原始 HTTP 请求体 UTF-8 字节的 `payloadSha256`。接收方回执另含 `outcome`，可证实业务效果已提交时为 `applied`，其余未确认结果为 `unknown`。租户和订阅标识由可信业务记录或接收方的已验证路由关联。保存摘要和标识，避免把业务正文或签名密钥写入演练回执。

```powershell
node eng/deploy/Compare-FullNetWebhookRecoveryEvidence.mjs $RestoredDeliveriesJson $ReceiverReceiptsJson
```

缺失回执、身份或摘要冲突、结果未知以及仅接收方存在的回执均需继续对账。工具只比较文件，会报告未解决项并以非零状态退出。签发 `dispatch.json` 前，由负责人确认对账范围完整，并复核每项已确认外部效果在恢复库中的处置结果。

对已匹配、接收方业务效果已提交且恢复库仍为 `pending` 或租约已到期 `processing` 的记录，保持 Worker 和外发隔离，在对应租户调用 `POST /api/v1/webhooks/subscriptions/{subscriptionId}/deliveries/{deliveryId}/reconciliations`。操作人需要订阅读取、投递读取及独立的 `webhooks.deliveries.reconcile` 权限。请求填写固定 UUID `operationId`、投递详情的原 `version`、匹配的 `eventId` 与 `payloadSha256`，以及从已验证的接收方证据文件原始字节计算的 `receiverReceiptSha256`。操作人负责核验权威来源并按操作标识保存该证据。服务核对当前租户、原投递和载荷摘要，并把证据摘要与操作者写入审计。

保存返回的对账回执，复核投递为 `delivered`、版本增加 1、已清除下次发送时间，并核对尝试次数和尝试记录。原操作人用相同操作标识和内容重试可取回原回执。若返回 409，重新读取投递并核对原证据，保持隔离直到差异解决。

处置入口接受 `pending`，以及按当前 Worker `LeaseSeconds` 判定租约已到期的 `processing`。领取中的记录先核对原 started 尝试和接收方权威证据；未到期租约、版本或摘要变化均返回 409，不能强行确认。最终更新再次检查租约和版本，旧 Worker 的结果写回不能覆盖已确认状态。确认不伪造 HTTP 成功结果，也不完成原 started 尝试；该尝试随后显示为 `unconfirmed`，恢复回执单独保存已核验的接收方证据。仍须先停止旧 Worker 并保持恢复环境外发隔离，租约到期本身不证明旧进程已经停止。

## 禁止事项

- 不要在生产直接修改业务表绕过模块边界
- 不要跳过 Migrator 手工执行未登记 SQL
- 恢复脚本不得输出或记录凭据原文
- 不要直接运行会启动全部 Worker 的发布流程后再补做外发和权限检查

## 账号挑战投递巡检

先完成挑战投递日记迁移并统一升级签发、消费实例，再在 Worker 配置 `Identity:AccountChallenges:Reconciliation:Enabled=true` 启用巡检。默认关闭；API 不注册巡检循环。`BatchSize` 默认 100，允许 1～500；`PollSeconds` 默认 60 秒，允许 30～3600 秒，配置更新在下一轮读取，无效值拒绝启动或暂停本轮并记录固定诊断。

巡检每轮按当前数据库的主键顺序读取一页，结束后从头扫描；重启丢失内存游标也可幂等重扫。它仅撤销已完成或到期的 unknown/rejected 挑战，不重发邮件，不推断实际送达，不改写 accepted、旧版空状态或有效在途记录。多实例使用按标识 CAS 收敛；失败保持游标，下次重试该页，已完成的行不重复修改。

日志事件 4533 记录扫描和对账数量，4534 表示本页失败，不包含地址、摘要、明文凭据或原始数据库异常。全表遍历时间取决于数据规模、批大小与间隔；有限的功能样本不代表大表扫描成本或容量已验证。巡检完成不代表旧版空状态已获得受理证据；回退或混跑旧实例仍遵守停止签发并排空/等待过期的升级约束。

## 相关文档

- [应用升级](./application-upgrade.md)
- [F16 发布清单](../verification/f16-release-checklist.md)
