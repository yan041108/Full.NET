# Cache Redis 连接串启动校验验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/cache-redis-connection-validation`
- 隔离开发基线：`main@b43d2145b92f18d8fbc7b93a9ebec95197d6fc71`
- 最终同步基线：`main@3648321709fc8677b6f5e70cca5b330a3dd3ec5d`
- 状态：`Build-verified`

## 范围与行为合同

`AddFullNetCaching` 原本只校验缓存时长和抖动范围。配置了
`Cache:RedisConnectionString` 或后备的 `ConnectionStrings:redis` 后，连接串会直接进入
Redis 缓存与 Backplane 注册，畸形选项值直到后续解析或连接阶段才会暴露。

本切片在既有同步配置校验入口中复用 StackExchange.Redis 的连接串解析器，使畸形语法在服务注册阶段以
`OptionsValidationException` 失败。异常仅报告稳定配置键
`Cache:RedisConnectionString` 和通用格式原因，不包含连接串原值、密码或其他 Secret。

未配置 Redis 时仍保持纯内存缓存；合法连接串、缓存时长、Fail-Safe、健康检查、Backplane、指标和
`.AsHybridCache()` 行为不变。本切片不修改数据库、SQL、Redis 连通性探针、API、客户端或 canonical
测试门槛，也不占用 Docker。

## RED / GREEN

| 阶段 | 结果 |
| --- | --- |
| 基线 | `FusionCacheRegistrationTests` **1/1** 通过 |
| RED | 新增畸形布尔选项值场景后，聚焦 **1/2** 通过；目标用例因未抛出 `OptionsValidationException` 按预期失败 |
| GREEN | 接入官方连接串解析器并转换为脱敏配置异常后，聚焦 **2/2** 通过 |

首次使用 `localhost:not-a-port` 作为样例时，官方解析器将其保留为可延后解析的端点文本，不能证明连接串
语法错误。最终回归使用解析器明确拒绝的非法布尔选项值，并在撤回生产变更后重新确认 RED，再恢复最小实现。

## 验证范围

| 门禁 | 隔离分支新鲜结果 |
| --- | --- |
| `Full.NET.slnx` Release | **0 warning / 0 error** |
| Unit | **411/411**，失败 0、跳过 0 |
| Caching 注册聚焦 | **2/2**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Governance / Project Skill / Workspace | **11/11** / **52** 项 / 通过 |
| owned C# `dotnet format --verify-no-changes` | 通过 |
| `git diff --check` | 通过 |

本切片只收紧同步配置字符串解析，不创建 Redis 连接，不改变 Redis 健康检查、缓存读写或 Backplane
运行行为，因此最终验证不启动 Docker，也不重复运行数据库 Integration。紧邻前序 DatabaseOptions
已在同一最终基线完成完整 Integration **189/189**，失败 0、跳过 0、stderr 0。最终 canonical 为
**411/7/49/189**。

## 规则与 Skills 复盘

- 规则：这是单个配置输入遗漏，已由启动期自动回归阻断；没有重复事故、高风险不可逆影响、规则歧义或新架构
  决策证据，本次不新增或修改规则。
- Skills：本切片是局部配置校验，不属于完整缓存业务模型交付，也没有形成至少三个需工程判断的跨模块复用步骤；
  不更新 `fullnet-cache-feature` 候选，本次无 Skills 变化。
