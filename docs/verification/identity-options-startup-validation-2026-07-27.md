# Identity Options 启动校验补强（2026-07-27）

## 目标

补齐 `IdentityOptionsValidator` 与运行时消费者之间的配置约束，使下列无效配置在
`ValidateOnStart` 阶段被拒绝，而不是延迟到首次登录、会话变更、CORS 初始化、签名密钥装载
或 Seed 执行时才失败：

- 登录与会话变更限流额度小于 1；
- `SigningKeys`、`AllowedOrigins` 或 `Bootstrap` 被配置绑定为 `null`；
- `SigningKeys` 字典包含值为 `null` 的条目。

本切片不修改配置键名、默认值、公共 API、数据库对象、SQL、租户边界或测试 canonical 数量。

## 根因与运行时边界

Identity 模块已经通过 `ValidateOnStart` 注册 options 校验，但原 validator 只覆盖 token 生命周期、
锁定策略、活动签名密钥与远程超级管理员开关，未覆盖以下运行时前置条件：

| 配置 | 延迟失败位置 | 补强约束 |
| --- | --- | --- |
| `LoginRateLimitPermitLimitPerMinute` | 登录限流器首次构造 | 必须大于等于 1 |
| `SessionMutationRateLimitPermitLimitPerMinute` | 会话变更限流器首次构造 | 必须大于等于 1 |
| `SigningKeys` | RSA 密钥环读取 `Count` 或遍历 | 集合不得为 `null`，条目值不得为 `null` |
| `AllowedOrigins` | CORS 与允许来源校验初始化 | 集合不得为 `null` |
| `Bootstrap` | Host 管理员 Seed Contributor 读取 | 对象不得为 `null` |

合法但为空的 `AllowedOrigins` 仍保持原语义；未启用 token endpoint 时，空 `SigningKeys` 仍允许。
本次只拒绝运行时消费者无法处理的结构和值域，不扩大既有功能开关语义。

## 测试先行证据

| 阶段 | 新增断言触发的失败 | 最小实现后的结果 |
| --- | --- | --- |
| RED 1 | `LoginRateLimitPermitLimitPerMinute = 0` 未被拒绝 | 登录与会话变更额度下限均在启动期校验 |
| RED 2 | `SigningKeys = null` 未被拒绝 | 三个必需集合/对象均显式拒绝 `null` |
| RED 3 | 活动密钥有效但字典另含 `null` 条目时 validator 错误放行 | 拒绝任意 `null` 密钥条目，并使活动密钥查询保持空安全 |

三轮均复用现有两个 `IdentityOptionsValidatorTests` 测试方法，因此 Unit canonical 保持 400。
最终聚焦测试为 **6/6**，失败 0、跳过 0。

## 隔离分支预验证

| 命令或门禁 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release --nologo` | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **400/400** / **7/7** / **49/49**，失败 0、跳过 0 |
| `pnpm test:naming` | **23/23** |
| `pnpm test:governance` | **11/11** |
| `pnpm test:skills` | `fullnet-module-delivery` **52** 项合同检查通过 |
| `pnpm test:workspace` | 退出码 0 |
| `pnpm test:integration:partitions` | SQL Server API 35 + MySQL API 35 + Migrations 62 + Infrastructure 57 = **189**，无遗漏或重复 |
| `dotnet format ... --verify-no-changes`（两份受控 C# 文件） | 退出码 0 |
| `git diff --check` | 退出码 0 |

首次使用 `--no-restore` 的全解构建因新 worktree 尚无 7 个 `project.assets.json` 而退出；
按诊断执行带还原的同一 Release 构建后成功。该环境准备问题未通过修改代码或放宽门禁规避。
首次格式检查还发现全局 Git `core.autocrlf=true` 生成的 CRLF 与仓库 `.editorconfig` 的 LF 要求不一致；
由 `dotnet format` 仅规范两份受控 C# 文件后，使用同一检查命令复验通过。

## 最新 main 同步复验

IdentityOptions 提交已线性同步到 Files 切片完成后的
`main@fd3257dbfb0000389b24acc1e9370e7196a33c5d`，无冲突。同步后的新鲜证据如下：

| 命令或门禁 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release --nologo` | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **404/404** / **7/7** / **49/49**，失败 0、跳过 0 |
| Naming / Governance / Project Skill / Workspace | **23/23** / **11/11** / **52** 项 / 退出码 0 |
| Integration 分片发现 | API SQL Server 35 + API MySQL 35 + Migrations 62 + Infrastructure 57 = **189** |
| 两份受控 C# 文件格式检查 / `git diff --check` | 退出码 0 |

最终 canonical 保持 **404/7/49/189**；本切片未新增测试方法，也未修改四处 canonical 来源。
rebase 后 Git checkout 再次按全局配置物化 CRLF，已用相同的窄范围格式化命令规范并确认 Git 内容
无额外差异。

## Docker 与双库边界

本切片只修改 Identity options 的纯内存 validator 与 Unit 测试，不改变 SQL、迁移、持久化、
API 契约或数据库提供程序行为，因此未占用 Docker，也未重复运行 Integration 容器测试。
Integration 分片发现门禁确认既有 **189** 项分区完整；SQL Server/MySQL 运行证据由同一合并队列中
拥有 Docker 的 Jobs 切片独占提供。

## 规则与 Skills 复盘

- 现有 `AGENTS.md` 与 `rules/development-quality.md` 已要求配置在启动边界验证，并要求行为变更先建立
  可失败验证；本次是一次局部 validator 覆盖遗漏，已由 Unit 回归锁定，未达到新增或修改规则门槛。
- 本次没有形成跨模块重复且稳定的新工程判断流程，`fullnet-module-delivery` 也未暴露真实缺口；
  因此无 Skills 变化。
