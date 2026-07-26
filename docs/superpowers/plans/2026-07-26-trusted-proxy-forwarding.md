# 可信代理转发边界实施计划

**目标：** API 只接受显式可信代理提供的客户端地址与协议，并让日志、限流、
Origin 校验和认证审计统一消费规范化后的连接信息。

**架构：** `Full.NET.Hosting` 拥有配置、启动校验和 ASP.NET Core
`ForwardedHeadersMiddleware` 适配。API 在所有地址、协议消费者之前启用该边界；
业务模块禁止直接解析 `X-Forwarded-*`。

**状态：** 已完成（2026-07-26）。

## 全局约束

- `TrustedProxy` 默认关闭，未启用时忽略所有转发 Header。
- 只处理 `X-Forwarded-For` 与 `X-Forwarded-Proto`。
- 启用时至少配置一个精确代理 IP 或 CIDR，`ForwardLimit` 为 1～10。
- 无效地址、全地址族 CIDR，以及覆盖完整 IPv4-mapped 空间的 IPv6 CIDR 必须在
  启动期失败。
- 转发中间件必须先于本地化、请求日志、异常处理、CORS、限流、认证、授权和
  Endpoint。
- 不改变公共 HTTP、JSON、权限码、错误码、数据库结构或双数据库行为。

## Task 1：配置契约与启动校验

**文件：**

- `src/BuildingBlocks/Full.NET.Hosting/Forwarding/TrustedProxyOptions.cs`
- `src/BuildingBlocks/Full.NET.Hosting/Forwarding/TrustedProxyOptionsValidator.cs`
- `tests/Full.NET.UnitTests/Hosting/TrustedProxyOptionsTests.cs`

- [x] 先建立默认关闭、空信任源、无效 IP/CIDR、越界层数和全网 CIDR 的 RED。
- [x] 实现安全默认值、Options 绑定和 `ValidateOnStart()`。
- [x] 审查加固：拒绝 `::ffff:0:0/96` 及覆盖完整 mapped IPv4 空间的更宽 IPv6
  网络。
- [x] 聚焦 Unit 验证：

  ```powershell
  dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~TrustedProxyOptionsTests" --minimum-expected-tests 12
  ```

## Task 2：ASP.NET Core 适配与管道顺序

**文件：**

- `src/BuildingBlocks/Full.NET.Hosting/Forwarding/TrustedProxyForwardedHeadersConfigurator.cs`
- `src/BuildingBlocks/Full.NET.Hosting/Forwarding/TrustedProxyForwardingExtensions.cs`
- `src/Hosts/Full.NET.Host.Api/Program.cs`
- `src/Hosts/Full.NET.Host.Api/appsettings.json`
- `tests/Full.NET.ArchitectureTests/TrustedProxyBoundaryTests.cs`

- [x] 清除框架自带 loopback 信任默认值，禁用时不挂载中间件。
- [x] 启用时只登记显式 IP/CIDR，并只处理地址与协议。
- [x] 将转发中间件置于全部地址、协议消费者和 Endpoint 之前。
- [x] 用 Architecture 测试禁止生产模块直接解析转发 Header，并锁定全部消费者
  顺序。

## Task 3：攻击面、双栈、限流和双库审计

**文件：**

- `tests/Full.NET.IntegrationTests/Api/TrustedProxyForwardingTests.cs`
- `tests/Full.NET.IntegrationTests/Api/TrustedProxyForwardingAssertions.cs`
- `tests/Full.NET.IntegrationTests/Api/TrustedProxyForwardingApiSqlServerTests.cs`
- `tests/Full.NET.IntegrationTests/Api/TrustedProxyForwardingApiMySqlTests.cs`
- `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`

- [x] 覆盖默认关闭、伪造 Header、未知代理、可信 IP/CIDR、单层/多层链、层数上限、
  IPv4/IPv6、IPv4-mapped 精确代理/网段和无效地址。
- [x] 通过限流分区证明规范化后的客户端地址被下游消费。
- [x] 通过 SQL Server/MySQL 登录审计证明地址和协议进入 Identity。
- [x] 将允许来源设置为无关域名，并用 `http` 反证返回 403，证明成功路径依赖
  `X-Forwarded-Proto: https`。
- [x] 聚焦 Integration 验证：

  ```powershell
  dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter "FullyQualifiedName~TrustedProxyForwarding" --minimum-expected-tests 12 --timeout 20m
  ```

## Task 4：部署文档、门槛与全量验证

**文件：**

- `docs/development/getting-started.md`
- `docs/roadmap/capability-status.md`
- `docs/superpowers/plans/2026-07-18-architecture-hardening.md`
- `docs/verification/trusted-proxy-forwarding-2026-07-26.md`
- `docs/verification/test-threshold-audit-2026-07-19.md`
- `README.md`
- `.github/workflows/ci.yml`
- `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- `scripts/testing/run-integration-shard.mjs`

- [x] 记录 Aspire、Nginx、Kubernetes、双栈和错误配置风险。
- [x] 同步 Unit/Compatibility/Architecture/Integration 门槛为
  **378/7/49/184**，分片为 **35/35/62/52**。
- [x] 运行 Release 构建、四类 .NET 测试、Integration 分片发现/全量、Naming、
  Skills、Governance、`git diff --check` 和状态检查。
- [x] 完成规则、Skills 复盘和安全审查复核。
