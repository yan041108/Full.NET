# Realtime Redis Backplane 故障与恢复

本文说明 SignalR 多 API 节点使用 Redis Backplane 时的就绪信号、故障边界和恢复步骤。
它不把即时消息提升为可靠业务事件，也不替代业务查询或事务 Outbox。

## 配置与信号

优先使用独立配置，避免把 Realtime 的发布拓扑与缓存连接生命周期隐式绑定：

```json
{
  "Realtime": {
    "Enabled": true,
    "HubPath": "/hubs/notifications",
    "RedisBackplaneConnectionString": "redis:6379"
  }
}
```

未设置专用连接时可复用 `ConnectionStrings:redis`。生产连接由平台统一设置
`AbortOnConnectFail=false`，并使用
`fullnet:{environment}:signalr:` Channel Prefix；短暂中断后 API 节点会继续尝试重连。

- `/health/live`：只表示 API 进程存活，不检查 Backplane。
- `/health/startup`：只检查 Schema Contract，不检查 Backplane。
- `/health/ready`：配置 Backplane 后包含 `realtime-backplane`；Redis 不可达时返回
  503，恢复后自动回到 200。

健康检查使用独立短连接、两秒总预算、无重试 `PING`，不会输出连接字符串、异常堆栈
或 Redis 内部类型。

## 故障语义

Redis 中断时，同一 API 节点内的连接仍可能保持在线，但跨节点组播不可用。发布调用
可能快速抛出 Redis 异常，也可能只表现为远端未收到；调用返回不得作为最终交付证明。

Realtime 消息是尽力即时下行：

- 客户端重连或收到异常后必须重新查询权威 HTTP API。
- 账号、权限、通知持久化等业务状态以数据库为准。
- 需要可靠传播的业务事件必须写入事务 Outbox，由消费者幂等处理。
- 不得通过无限重试 Realtime 发布来模拟可靠消息队列。

## 故障处置

1. 确认 `/health/live=200` 且 `/health/ready=503`，排除 API 进程故障。
2. 检查 `realtime-backplane` 健康项和 Redis 服务端连通性；不要在工单或日志中复制
   含密码的完整连接字符串。
3. 检查 Redis 端点、TLS、认证、网络策略和环境级 Channel Prefix 是否一致。
4. 恢复 Redis 后持续观察所有 API 副本的 `/health/ready`，应自动收敛为 200；
   正常情况无需重启 API。
5. 从一个 API 节点建立已认证 Hub 连接，在另一节点发布测试机器码，确认跨节点收到；
   测试数据不得触发真实业务副作用。
6. 若 Redis 已可达但个别节点持续 503 或无法跨节点投递，先保留该节点连接失败日志和
   指标，再滚动替换异常副本；不要同时重启全部副本。

## 自动化证据边界

SQL Server/MySQL 集成测试使用两个独立 API 宿主、专用 Redis 8.6 容器和真实 SignalR
Long Polling 客户端，覆盖故障前跨节点投递、Redis stop/start、双节点 ready
503→200 和无宿主重启恢复。该证据支持 `Build-verified`，但不代表生产编排器、网络抖动、
Redis Cluster/Sentinel、TLS、告警路由或管理端客户端已经完成验收。
