# 限流策略错误码冲突注册验证记录（2026-07-27）

## 范围

本切片收紧 Hosting 统一 429 响应的稳定机器码注册边界：

- 同一限流策略首次注册错误码时写入映射；
- 同一策略重复注册相同错误码时保持幂等；
- 同一策略注册不同错误码时立即失败，并保留首次映射。

本切片仅修改 `RateLimitPolicyErrorCodes`、既有 Hosting Unit 测试与本验证记录，
不改变限流算法、配额、分区键、客户端、数据库或 Docker 场景。

## TDD 证据

| 阶段 | 命令 | 结果 |
| --- | --- | --- |
| 基线 | `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~RateLimitPolicyErrorCodesTests"` | 1/1 |
| RED | 同上，新增冲突注册测试后运行 | 1/2；预期的 `InvalidOperationException` 未抛出 |
| GREEN | 同上，加入冲突保护后运行 | 2/2 |

Unit canonical 在初始隔离基线由 **406 → 407**；同步最终主线后由
**408 → 409**。

## 验证

| 门槛 | 结果 |
| --- | --- |
| Release solution | 隔离分支与最终同步源码均为 0 warning / 0 error |
| Unit | 隔离分支 407/407；最终同步源码 409/409 |
| Compatibility | 7/7 |
| Architecture | 49/49 |
| OpenAPI / breaking | 58/58；25 份基线与当前契约兼容 |
| Governance / Skill | 11/11；52 项契约检查 |
| Workspace / diff | 通过 |
| owned C# format | 两份本切片文件限定 `dotnet format --verify-no-changes` 通过 |
| Integration | 继承紧邻 Tenancy 最终全量 189/189；本切片不占 Docker |

全仓 `dotnet format --verify-no-changes` 会被 Windows checkout 中非本切片的存量
CRLF/格式差异阻断；本切片没有改写或纳入这些无关文件。

## 规则与 Skills 复盘

- 规则：这是一次稳定机器码注册边界的局部缺口，已由确定性 Unit 测试自动防护；
  现有公共契约与测试先行规则已覆盖，本次不新增或修改规则。
- Skills：本切片没有形成跨模块重复且需要多步工程判断的新流程，也未暴露
  `fullnet-module-delivery` 缺口，本次无 Skills 变化。
