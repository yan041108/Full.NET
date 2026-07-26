# Identity 模块注册职责拆分验证记录

- 日期：2026-07-27
- 基线：`main@40976e5c0327943eb644e0c9867759548c02a5b5`
- 范围：调整 Identity 组合根内部注册结构并新增 2 项 Unit Test；不修改公共 API、Endpoint、SQL、迁移、Dapper、生产客户端或 Integration 测试方法。真实栈复验同时修正既有 Playwright 辅助函数对旧 Vue 外壳的定位，不增加测试数。

## 1. 行为边界

`IdentityModule` 仍是唯一公开模块入口，并保持 `AddMigrationServices` 在前。其余注册依次委托给四个 `internal` 扩展：

1. Authentication：Session/JWT、Data Protection、TOTP、API Key、RSA/Token 与三个命名 Scheme。
2. Authorization：授权目录、权限快照、Session Context、Navigation、DataScope、Policy Provider 与 Result Handler。
3. Domain：本地化、错误资源、Host 管理服务、目录映射、验证器、Command Handler 与 Cookie Writer。
4. HTTP Policies：来源校验、CORS、限流与 System.Text.Json Context。

重复注册通过 Identity 内部认证标记避免重复添加命名 Scheme；授权替换检查最终有效描述符，确保调用者在两次模块注册之间插入自定义 Provider/Result Handler 时仍恢复 Full.NET 的有效实现。CORS 与 JSON 配置器均以可枚举幂等注册，JSON Context 保持首位且唯一。

## 2. RED / GREEN 与结构审查

| 阶段 | 证据 |
| --- | --- |
| RED | 四个 `AddIdentity*` 内部扩展不存在时测试项目编译失败。 |
| 中间回归 | 重复认证注册在解析 `AuthenticationOptions` 时暴露重复 Scheme；冻结快照暴露授权 `Replace` 顺序漂移；独立审查补充发现后置自定义授权描述符会成为有效实现。 |
| GREEN | 内部认证标记、冻结描述符序列和“最后有效授权描述符”判断分别关闭上述缺口。 |
| 独立审查 | 三轮结构化审查最终结论为 Ready，无 Critical/Important 遗留。 |
| 真实栈测试护栏 | 首次运行先命中 Element Plus 隐藏 option，切换到既有辅助函数后又暴露其仍定位旧 `.tenant-card`；辅助函数改为打开 Art Design 用户菜单并在 `shell-tenant-select` 内断言可见选中值，Layui 保持既有上下文定位。 |

规则复盘未发现需要固化为全仓规则的新型遗漏；Skill 复盘未命中可重复模块交付流程的升级门槛，因此本切片不修改规则或项目 Skill。

## 3. 最终验证

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore Full.NET.slnx --locked-mode` | 通过；lockfile 未变化 |
| `dotnet build Full.NET.slnx -c Release --no-restore` | **0 warning / 0 error**，17.24s |
| Unit / Compatibility / Architecture | **398/398** / **7/7** / **49/49**，失败 0、跳过 0 |
| Identity SQL Server/MySQL 聚焦 | 登录、刷新/上下文竞态、角色数据范围与 Development Seed **8/8**，失败 0、跳过 0，2m34s |
| Vue/Layui 真实栈登录 | `auth-smoke.spec.mjs` **2/2**，失败 0，38.9s |
| OpenAPI / breaking | **58/58** / **25/25**；breaking 基线为 `40976e5` |
| Governance / Skill / workspace | **11/11** / **52** / 通过 |
| 格式与范围 | 定向 `dotnet format --verify-no-changes`、`git diff --check` 通过；无 SQL、迁移、公共 API 或生产客户端改动 |

最终 canonical 为 **398/7/49/189**。Integration 总数保持 189，本切片未新增或删除 Integration 测试方法；客户端事实保持 client-contracts **76**、Vue **201**、Layui **95**。

功能提交 `f389fe1` fast-forward 合入 `main` 后再次验证 Release **0 warning / 0 error**、Unit/Compatibility/Architecture **398/7/49**、Governance **11/11**、Skill **52** 与 workspace；任务分支、Git worktree 注册和物理残留均已删除。
