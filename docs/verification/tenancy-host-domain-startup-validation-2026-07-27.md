# Tenancy HostDomains 启动校验验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/tenancy-host-domain-validation`
- 初始基线：`main@e34ce5cc9bbe5b753baef6fb304108e207173c04`
- 最终同步基线：`main@b43d2145b92f18d8fbc7b93a9ebec95197d6fc71`
- 功能提交：`d49b2973030a705532d027788ae1ee33e5eabfa4`
- 状态：实现、最终门禁、main 合入与分支/工作树清理均已完成

## 范围与契约

`Tenancy:HostDomains` 参与 Host 与租户请求的安全边界判断。原注册只绑定配置，
无效值会延迟到请求路由阶段表现为租户解析偏差。本切片为真实模块注册入口增加
`ValidateOnStart`，并拒绝：

- 空白项或带首尾空白的项；
- 带协议、端口、路径或通配符的项；
- 按大小写不敏感规则重复的项。
- 被后置配置器替换为 `null` 的集合。

合法的 DNS 主机名、`localhost`、IPv4 与无方括号 IPv6 地址继续可用；空集合保持原有语义。
校验器不自动修剪、改写或合并配置，避免部署配置与实际请求匹配规则出现隐式差异。
本切片不修改公共 API、数据库对象、SQL、客户端、租户持久化或缓存键。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 基线 | 既有租户解析中间件回归 **6/6** |
| RED 1 | 新测试经真实 `TenancyModule.AddMigrationServices` 构造并启动宿主；旧实现未抛出 `OptionsValidationException`，新增用例 **0/1** |
| RED 2 | 后置配置器将集合替换为 `null` 时，初版校验器抛出 `NullReferenceException`，未形成可诊断的配置失败 |
| GREEN | 注册显式 `IValidateOptions<TenancyOptions>`、`ValidateOnStart` 与空集合守卫后，新用例 **1/1**；连同既有租户解析回归 **7/7** |

首次聚焦构建前的新 worktree 缺少 NuGet assets，完成正常 restore 后建立基线；该环境准备
问题发生在测试执行前，不计作行为 RED。测试夹具最初使用 `await using` 持有只实现
`IDisposable` 的 `IHost`，编译失败后改为 `using`；有效 RED 以测试成功编译并命中旧行为为准。

## 隔离验证

| 门禁 | 结果 |
| --- | --- |
| Unit 工程 Release 构建 | 0 warning / 0 error |
| 新启动校验测试 | **1/1**，失败 0、跳过 0 |
| 新测试 + 既有租户解析回归 | **7/7**，失败 0、跳过 0 |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit / Compatibility / Architecture | **407/407** / **7/7** / **49/49**，失败 0、跳过 0 |
| Naming / Governance / Project Skill | **23/23** / **11/11** / **52** 项合同检查通过 |
| Workspace | 退出码 0 |
| Integration 分片发现 | SQL Server API 35 + MySQL API 35 + Migrations 62 + Infrastructure 57 = **189**，无遗漏或重复 |
| 三份受控 C# 文件格式检查 | 退出码 0 |

## 最新 main 同步复验

功能提交已无冲突线性重放到 Outbox 切片完成后的
`main@b43d2145b92f18d8fbc7b93a9ebec95197d6fc71`。同步后的新鲜证据如下：

| 门禁 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit / Compatibility / Architecture | **408/408** / **7/7** / **49/49**，失败 0、跳过 0 |
| 新测试 + 既有租户解析回归 | **7/7**，失败 0、跳过 0 |
| Naming / Governance / Project Skill | **23/23** / **11/11** / **52** 项合同检查通过 |
| Workspace | 退出码 0 |
| Integration 分片发现 | SQL Server API 35 + MySQL API 35 + Migrations 62 + Infrastructure 57 = **189**，无遗漏或重复 |
| 全量 Integration | **189/189**，失败 0、跳过 0，耗时 **30m07s**，stderr 0 |
| 三份受控 C# 文件格式检查 / `git diff --check` | 退出码 0 |

最终 canonical 为 **408/7/49/189**；README、本地开发指南、CI、项目 Skill 交付地图与
测试门槛审计记录已同步。全量 Integration 期间独占 Docker，完成后确认测试容器与
Integration 进程均已退出。

## main 合入后复验与清理

功能提交通过 `--ff-only` 合入 main。main 自身输出目录的新鲜复验结果为：

- Unit 工程 Release 构建 0 warning / 0 error；
- Unit **408/408**，Tenancy 新旧聚焦 **7/7**；
- Governance **11/11**、Project Skill **52** 项、Workspace 均通过；
- `git diff --check` 通过，Docker 容器与 Integration 进程均为 0。

`codex/tenancy-host-domain-validation` 分支、Git 工作树登记和物理目录均已删除。
主检出区只保留用户原有的 `.cache/` 与 `.tmp/art-design-pro/` 未跟踪目录。

## 规则与 Skills 复盘

- 规则：项目模块交付地图已明确要求新增配置或外部资源边界必须同时提供启动期校验、
  失败路径测试和运行时断言。本次是既有约束下的单模块遗漏，已由真实宿主启动 Unit 回归锁定；
  未发生第二次同类规则歧义、高风险事故或用户长期决策，本次无规则变化。
- Skills：`fullnet-module-delivery` 已给出准确的启动校验交付路径，本次未暴露触发词、步骤或
  异常路径缺口，也没有形成可与现有 Skill 分离的三个以上稳定判断步骤；本次无 Skills 变化。
