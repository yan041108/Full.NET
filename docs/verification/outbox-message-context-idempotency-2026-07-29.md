# Outbox 消息上下文与幂等门禁验证

- 日期：2026-07-29
- 状态：Build-verified
- 任务基线：`df21eb40b9c8ce646c954144880f1da9922277de`
- 范围：Task 24 Step 1；容量矩阵与默认并发决策仍开放

## 交付边界

1. `IntegrationEventContext` 向 Handler 暴露持久化记录中的 `MessageId`、`MessageType`、
   `SchemaVersion`、`TenantId`、`TraceId` 和 `OccurredAtUtc`。
2. Worker 统一调用上下文重载；payload-only Handler 通过默认接口实现继续工作，不要求
   一次性修改全部签名。
3. 每个生产 Handler 必须声明 `NaturallyIdempotent` 或
   `MessageIdDeduplication`；`Unspecified` 和未知枚举值在 Worker 启动期失败。
4. 当前租户缓存失效 Handler 声明天然幂等：重复删除和重复失效广播只会收敛到相同缓存
   缺失状态，不会创建新的业务事实。
5. 策略声明不改变至少一次投递。跨数据库写入或外部副作用若选择
   `MessageIdDeduplication`，仍必须在实际副作用提交边界持久化 MessageId。

## TDD 证据

RED 使用聚焦 Unit 首先引用尚不存在的上下文与策略类型，编译按预期失败：

```text
CS0246 IntegrationEventContext
CS0246 IntegrationEventIdempotencyStrategy
```

GREEN 后直接运行测试 DLL，避免项目级 `dotnet test` 在当前 MTP 路径中把过滤表达式解析为
零测试：

| 验证 | 结果 |
| --- | --- |
| Handler Matcher | **5/5**，失败 0、跳过 0 |
| 精确路由与上下文透传 | **1/1**，失败 0、跳过 0 |
| Outbox/Matcher/Tenancy Unit 影响集 | **28/28**，失败 0、跳过 0，约 8 秒 |
| Naming/Dependency Architecture 影响集 | **34/34**，失败 0、跳过 0，约 7 秒 |
| Unit discovery | **510**，canonical 更新为 **510/7/49/199** |
| Unit 与 Worker Release 构建 | 0 warning、0 error |
| 受影响 Integration | Outbox **32/32**、Smoke **8/8**、Tenancy **14/14**，合计 **54/54** |
| 性能治理 / 规则治理 | **3/3** / **13/13** |
| 目标文件格式 | `dotnet format --verify-no-changes` 通过 |

## 未改变项

- 未修改 Outbox 表、迁移、SQL、租约、续租、重试、死信或默认 `MaxConcurrency=1`。
- 未声明 Exactly-Once；进程崩溃、网络分区和终态确认竞态仍可能产生重复投递。
- 完整 **199** 项 Integration 不在本地运行，只由 `main` CI 四分片执行。

## 规则与 Skills 复盘

- 现有架构规格与性能规则已明确要求至少一次消费者使用稳定 EventId/MessageId 或业务幂等键，
  本次把它落成公共契约、启动校验和测试，不新增近义规则。
- `fullnet-performance-hardening` 已覆盖 Outbox 正确性、双库影响集与容量停止条件；本次只机械
  同步 canonical Unit 数量，无新的稳定工作流或 Skill。
