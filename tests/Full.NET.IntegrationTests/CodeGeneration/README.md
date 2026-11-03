# CodeGeneration 真实宿主 HTTP 验收

使用实际 Host.Api 入口、官方模块装配、DbUp、真实数据库、真实授权和文件工作区。`CodeGenerationApiSqlServerTests` / `CodeGenerationApiMySqlTests` 覆盖预览、模板、目录及运行四类契约；`CodeGenerationHttpLifecycleTests` 使用 Kestrel 回环动态端口及实际登录签发的 JWT，不使用 HTTP 响应替身或认证替身。

```powershell
node scripts/testing/run-build-with-resources.mjs --cpu 4 --memory-gib 4 -- dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-incremental '-m:4'
node scripts/testing/run-codegeneration-http.mjs --selection contracts
node scripts/testing/run-codegeneration-http.mjs --selection lifecycle
# 需要覆盖完整控制面及来源约束恢复时，明确选择以下 14 项，不运行整个 Integration 套件。
node scripts/testing/run-codegeneration-http.mjs --selection acceptance
# 当前工作区联合验证，同时执行此前受编译问题影响的组织列表双库两项。
node scripts/testing/run-codegeneration-http.mjs --selection current-workspace
```

`contracts` 为现有双库 8 项，`lifecycle` 为新增双库 4 项；只需重验运行契约时使用 `--selection runs`（2 项）。入口使用当前构建，不自动重编译；两批之间改变受验源码时须重新构建。不要把本入口替换为整个模块或仓库测试。

`--selection repair` 聚焦生命周期 4 项和 `Migration292CodeGenerationRunSourceRecoveryTests` 双库 2 项。迁移测试使用官方 045/046/051/292 脚本，验证旧结构升级、内联 Apply、残缺模板对拒绝、脏数据失败保留原约束，以及约束部分缺失后的未记账重跑。

`acceptance` 为 `contracts` 与 `repair` 的并集，共 14 项。额外属性、坏 JSON、错误 Content-Type 必须在创建运行前拒绝，并返回带正确状态的 ProblemDetails；原始请求内容不得进入错误正文。真实角色权限撤销后，旧令牌及重新登录均不能下载，管理员仍能读取原始包。

`current-workspace` 共 16 项，在上述范围加入 `OrganizationAssignmentListQueryTests.cs` 中的 `OrganizationAssignmentListApiSqlServerTests` / `OrganizationAssignmentListApiMySqlTests`。组织用例包含服务查询、接口及数据范围边界，单独计数。过滤、最低数和超时来自统一测试矩阵；执行器在成功上下文回收前解析原始 TRX，数量不足、失败、跳过和未执行均不能计为通过。

只复验完整接入、全局回滚和重启下载时使用 `--selection chain`（双库 2 项）。撤销权限保留现行安全戳轮换：旧令牌返回 401，重新登录但无下载权限返回 403。角色的动作权限须同时具备所属页面读取权限，不通过削弱该规则构造测试账号。

当前工作区验收必须按上面的正式工程命令重建完整项目依赖，保留组织测试，不使用冻结 DLL、`BuildProjectReferences=false` 或临时 Compile 排除；资源不足时正常排队，不以旧结果替代。前次限定依赖的历史证据与本轮重新构建结果分别见[整改执行记录](../../../docs/superpowers/plans/2026-10-24-codegeneration-remediation.md)，不能混用。

当前重建中修复的 Identity 协议 HTTP 重试边界使用 Unit 矩阵中的 `identity-protocol-http-safety` 聚焦集（3 项）。先正常构建 Unit 工程，再通过 `node scripts/testing/run-dotnet-test-suite.mjs unit --selection identity-protocol-http-safety --no-build` 执行；该集合使用真实客户端工厂、模拟传输失败，不属于真实网络 HTTP 的场景计数。

生命周期覆盖审查前候选编译、完整目标文件、审查后人工漂移、非法/额外输入、幂等、跨实体全局回滚、模板编辑/删除及宿主重启后的包字节一致性、Apply 关闭和到期下载。恢复场景仅在数据库终态完成语句注入一次 IOException，其他 SQL、事务、迁移和 HTTP 均为真实路径；新宿主通过显式恢复接口完成同一运行，重放不得新建 Apply。到期场景只推进归档存储时钟，不修改身份、令牌验证或业务时钟。

执行器只使用本批带随机归属标签的 SQL Server/MySQL/按需 Redis 容器；限额、ACK 和回收复用 `bounded-testcontainers-reaper.mjs`。需要本地已有对应镜像；缺失时明确失败，不静默改为共享实例。成功后由测试运行上下文清理本批临时结果，失败仅保留定位所需原始记录。不会清理此前聊天留下的目录、其他会话的容器或用户业务数据库。

Kestrel 动态端口用法依据 [.NET 10 WebApplicationFactory.UseKestrel](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0)。这些是本地 JIT 宿主证据，不外推生产部署、远程 Git 发布或整个宿主 Native AOT。
