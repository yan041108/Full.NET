# 执行清单 56 — MQTT 受控发布与控制面 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **56**（首个受控发布场景、客户端/消息记录查询；主题 ACL、载荷/速率/幂等；**不是** Kafka Provider 换名）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/mqtt`（`status` / `clients` / `messages` / `messages/publish`） |
| 发布 | `MqttMessagePublishService` + `MqttBrokerPublisher`；`MqttTopicAccessPolicy`（Host `fullnet/*`、租户 `tenants/{id}/*`、前缀白名单、禁 `#`/`+`） |
| 限额 | `MqttBrokerOptions`（`MaximumPayloadBytes`、`MaximumPublishRatePerMinute`）；`MqttPublishRateLimiter` |
| 幂等 | `IdempotencyKey` + `MqttSql.FindMessageByIdempotency`（Host/租户作用域）；`MqttHostIdempotencyConcurrencyTests` |
| Vue | `MqttControlPlaneView`（Host 上下文、Broker 状态、发布表单、客户端/消息表） |
| 权限 | `mqtt.broker.read` / `mqtt.clients.read` / `mqtt.messages.read` / `mqtt.messages.publish` |
| 契约 | `mqtt-control-plane-v1.json`；`platform-backup-crypto-mqtt-observability-contract.test.mjs` §MQTT |
| 数据 | 迁移 208 Host 幂等作用域等（见 `MqttHostIdempotencyConcurrencyTests`） |

## 清单 56 验收结论

- **已有**：控制面只读目录 + 受界 `POST publish`；Kafka 仍走 Messaging Integration Event，无 MQTT→Kafka 别名。
- **本槽**：`phase-c-56-mqtt-control-plane.spec.mjs`（status/clients/messages、非法主题 `mqtt.topic.forbidden` 或 Broker 未启用 409、页面冒烟）。
- **未验**：真实 Broker TCP/TLS 发布成功（需 `Mqtt:Broker:Enabled` 与可达 broker；dev 常 disabled）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`MqttTopicAccessPolicy` / `MqttPublishRateLimiter` / `MqttHostIdempotency` | **14/14**（`d40de4e6`） |
| `node --test` `platform-backup-crypto-mqtt-observability-contract.test.mjs` | **4/4** |
| real-stack | `phase-c-56-mqtt-control-plane.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
