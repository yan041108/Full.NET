# Outbox Handler 路由元数据验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/outbox-handler-route-validation`
- 初始基线：`main@2d6e13affd86be03cf1521be24ca56e58177a01c`
- 最终同步基线：`main@c828a6579fb36761581f62246ff83bef48d59635`
- 功能提交：`b727ec2`
- 合并提交：`b46ddd2d95feab7a9d5987211b33efdb1bcefb70`
- 状态：实现、最终门禁与 `main` 合并已完成，隔离分支/工作树按收口流程清理

## 范围与合同

本切片硬化 Worker 启动期的 Integration Event Handler 路由校验：

- `EventType` 必须包含非空白消息类型；
- `SchemaVersion` 必须为正整数；
- `LegacyEventTypes` 不得包含空白别名；
- 既有 `(MessageType, SchemaVersion)` 精确路由、并行版本和旧别名兼容语义保持不变；
- 非法处理器注册在 Worker 启动时失败，不等待消息进入 Outbox 后再变成死信。

本切片不改变数据库结构、Outbox 持久化格式、消息载荷、API、客户端、重试/死信语义或
canonical 测试数量。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| RED | 聚焦 4 项中新增元数据场景按预期失败：空白 `EventType` 未抛异常；其余既有 3 项通过 |
| GREEN | 同一聚焦命令 **4/4**，失败 0、跳过 0 |
| 回归边界 | 同一消息类型的 SchemaVersion 1/2 仍可并行；不同处理器抢占同一 canonical/legacy 路由仍被拒绝 |

## 当前验证

| 门禁 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit | **400/400**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Integration | **189/189**，失败 0、跳过 0，**28m33s**，stderr 0 |
| Naming | **23/23** |
| OpenAPI / breaking | **58/58** / **25/25** |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace | 通过 |
| Integration tooling | **4/4** |
| Integration 分片发现 | **35 + 35 + 62 + 57 = 189**，无遗漏或重复 |

## 规则与 Skills 复盘

- 规则：本次暴露的是已有启动校验未覆盖全部路由元数据，已用同一启动入口和 Unit 回归
  直接自动化；没有重复遗漏、规则歧义或高风险事故证据，本次不新增或修改规则。
- Skills：本切片是单一 Outbox 路由校验硬化，没有形成三个以上需要工程判断的高复用流程，
  也未暴露 `fullnet-module-delivery` 缺口，本次无 Skills 变化。

## 状态结论

本切片达到 `Build-verified`：非法 Handler 路由会在 Worker 启动期失败，既有精确版本路由、
并行版本和 legacy alias 兼容语义保持不变；最新主线上的完整 189 项 Integration 与静态
门禁均已通过。生产版本退役扫描、受控重放自动化和多副本压力基准不在本切片范围，仍不得
据此标记 Outbox 整体为 `Verified`。
