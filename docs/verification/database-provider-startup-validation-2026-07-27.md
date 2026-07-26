# Database Provider 启动校验验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/database-provider-startup-validation`
- 初始基线：`main@b43d2145b92f18d8fbc7b93a9ebec95197d6fc71`
- 最终同步基线：`main@85a1ddff125d4f0ea722227e5ae75a189153e656`
- 状态：最终验证完成，待 fast-forward 合入 `main`

## 范围与契约

本切片补齐 `DatabaseOptions.Provider` 的启动配置校验：

- 只允许 `SqlServer` 与 `MySql` 两个已支持 Provider；
- 数字形式绑定出的未知枚举值必须在 `IOptions<DatabaseOptions>` 启动校验阶段失败；
- 合法 Provider 的连接串、命令超时和 MySQL UUID 存储模式语义保持不变；
- 不修改 SQL、数据库迁移、连接创建逻辑、公共 API 或 canonical 测试数量。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 基线 | `MySqlConnectionStringPolicyTests` **14/14** |
| RED | 将 `int.MaxValue` 绑定为 `Database:Provider` 后，读取 `IOptions<DatabaseOptions>.Value` 未抛异常；聚焦套件 **13/14**，失败 1、跳过 0 |
| GREEN | 在现有 Options 验证链显式限定受支持 Provider 后，同一聚焦套件 **14/14**，失败 0、跳过 0 |

## 验证计划

本切片不新增测试方法，因此 canonical 数量保持不变。隔离分支先执行 Release、Unit、
Compatibility、Architecture、治理与格式门禁；`Full.NET.Data.Dapper` 属于共享基础设施，
最终同步前序队列后的最新 `main` 后，必须独占 Docker 执行完整 Integration 189，再合入清理。

| 隔离分支预验证 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit | **407/407**，失败 0、跳过 0 |
| Data 配置聚焦 | **14/14**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Naming | **23/23** |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace / owned C# 格式 | 通过 |

## 最终验证

同步前序队列后的最终 `main` 后，重新执行了完整门槛。canonical 保持
**410/7/49/189**，本切片不新增测试方法。

| 最终验证 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit | **410/410**，失败 0、跳过 0 |
| Data 配置聚焦 | **14/14**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| 完整 Integration | **189/189**，失败 0、跳过 0，25m08s，stderr 0 |
| Integration 分片发现 | **35/35/62/57 = 189**，无遗漏或重复 |
| Naming | **23/23** |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace / owned C# 格式 / diff check | 通过 |
| Docker / Integration 进程 | 0 |

## 规则与 Skills 复盘

- 规则：已有开发质量规则已要求配置启动校验、真实测试发现数量与共享基础设施全量
  Integration；本次缺口已由同一 Options 入口和 Unit 回归自动阻断，没有规则歧义或新的高风险
  边界，因此不新增或修改规则。
- Skills：本切片是单一 Options 封闭枚举校验，没有形成至少三个需要工程判断且跨任务稳定复用的
  工作流，也不属于 `fullnet-module-delivery` 纵向模块交付范围，本次无 Skills 变化。
