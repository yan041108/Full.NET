# Outbox 重复 Handler 路由验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/outbox-duplicate-handler-route`
- 初始基线：`main@085857c765d3e6ff5f33017eb09ffe5069db3fea`
- 最终同步基线：`main@c5f9b6ffdbbe8b853ef11113094f8e3ce62f41e2`
- 状态：`Build-verified`

## 范围与根因

Worker 在启动阶段调用 `IntegrationEventHandlerMatcher.ValidateUniqueRoutes`，用于在消费消息前
拒绝冲突的 `(EventType, SchemaVersion)` 路由。原校验仅在两个路由所有者的处理器类型名不同时
抛出异常；因此，同一处理器类型被依赖注入重复注册两次时会漏过启动校验。运行期匹配会返回
两个实例，并把本可在启动期发现的配置错误转成 `AmbiguousHandler` 死信。

本切片取消“类型名相同”的豁免：任意重复精确路由都必须在 Worker 启动期失败。同一消息类型的
不同 `SchemaVersion` 仍可并行，canonical 与 legacy alias 的精确匹配语义保持不变。

本切片不改变 Worker 消费流程、数据库结构、Outbox 持久化格式、载荷、重试/死信原因码、API、
客户端或 canonical 测试数量。

## RED / GREEN

| 阶段 | 结果 |
| --- | --- |
| 基线 | `IntegrationEventHandlerMatcherTests` **4/4** 通过 |
| RED | 添加同类型 Handler 重复注册场景后，聚焦 **3/4** 通过；目标用例因未抛出 `InvalidOperationException` 按预期失败 |
| GREEN | 删除同类型所有者豁免后，聚焦 **4/4** 通过 |

## 最终同步验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx --configuration Release --no-restore --nologo` | **0 warning / 0 error** |
| Unit | **407/407**，失败 0、跳过 0 |
| Handler matcher 聚焦 | **4/4**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace | 通过 |

未运行 Integration：变更只收紧纯内存 Handler 路由集合校验，不修改 Worker 处理流程、数据库
访问、SQL、迁移或提供程序行为；最新主线已继承 Logging 切片的完整 Integration **189/189**
证据，本切片不重复占用 Docker。

## 规则与 Skills 复盘

- 规则：根因已由既有启动校验入口和 Unit 回归自动阻断，没有重复遗漏、规则歧义或高风险事故
  证据，本次无新增或修改规则。
- Skills：本切片是单一条件缺陷修复，没有形成三个以上需要工程判断的高复用工作流，也未暴露
  `fullnet-module-delivery` 缺口，本次无 Skills 变化。

## 状态结论

同类型 Handler 的重复精确路由现在会在 Worker 启动阶段被拒绝，不再等到消息领取后产生
`AmbiguousHandler` 死信；既有版本并行与 legacy alias 兼容行为保持不变。
