# 可信代理客户端地址边界验证

- 日期：2026-07-26
- 状态：`Build-verified`
- 范围：API 转发 Header 信任边界、请求协议、请求日志/限流/Identity 审计消费路径、测试门槛与部署说明
- 实施计划：[可信代理转发边界实施计划](../superpowers/plans/2026-07-26-trusted-proxy-forwarding.md)

## 1. 交付结果

1. `TrustedProxy` 默认关闭；禁用时不挂载 Forwarded Headers Middleware，并清除 ASP.NET Core 自带的 loopback 信任默认值。
2. 启用时只处理 `X-Forwarded-For` 与 `X-Forwarded-Proto`，要求至少一个显式代理 IP/CIDR，代理链上限为 1～10。
3. 无效 IP/CIDR、`0.0.0.0/0`、`::/0`、覆盖完整 IPv4-mapped 地址空间的 IPv6 CIDR、禁用但残留信任源和启用但空信任源全部在启动期失败。
4. 转发中间件位于本地化、请求日志、CORS、限流、认证、授权和 Endpoint 之前；Identity 不直接解析 Header，继续读取规范化后的 `Connection.RemoteIpAddress`。
5. Aspire、Nginx 与 Kubernetes 配置边界已经写入[本地开发指南](../development/getting-started.md)，但实际生产等价发布拓扑仍需独立留证，因此本能力不标记为 `Verified`。

## 2. RED 与故障定位证据

| 场景 | RED 结果 | GREEN 结果 |
| --- | --- | --- |
| 配置/映射单元测试 | `Full.NET.Hosting.Forwarding` 尚不存在，Unit 编译失败；审查加固阶段 mapped 全网 CIDR 与更宽 IPv6 超网 **2** 项再次失败 | 聚焦 Unit **12/12** |
| API 管道未调用转发中间件 | 可信单层、多层与 IPv6 客户端均错误共享代理限流分区，聚焦 Integration **3** 项失败 | 代理链聚焦 Integration **8/8** |
| 原始 TestServer 登录请求 | 绕开转发 Header 后仍返回 400，证明失败来自手工请求体未进入既有 Minimal API JSON 绑定路径 | 改用仓库既有 `HttpClient + JsonContent`，测试 StartupFilter 只模拟 TCP 对端；Identity Origin/审计双库 **2/2** |
| 审查加固：协议断言独立性 | 将允许来源改为无关域名后，把 `X-Forwarded-Proto` 从 `https` 改为 `http`，SQL Server 用例按预期由 401 变为 403 | 恢复 `https` 后聚焦可信代理用例 **12/12**，证明 Origin 成功依赖协议规范化而非静态白名单 |

测试基础设施修正没有修改产品绑定器或 Endpoint，也没有为测试增加生产分支。

## 3. 新鲜验证

| 门禁 | 结果 |
| --- | --- |
| Release build | 通过，**0** 警告、**0** 错误 |
| Unit | **378/378**，失败 **0**、跳过 **0** |
| Compatibility | **7/7**，失败 **0**、跳过 **0** |
| Architecture | **49/49**，失败 **0**、跳过 **0** |
| Integration 聚焦 | 可信代理请求与双库审计 **12/12**，失败 **0**、跳过 **0** |
| Integration 分片发现 | SQL Server **35** + MySQL **35** + Migrations **62** + Infrastructure **52** = **184**，无遗漏或重复 |
| Integration 全量 | **184/184**，失败 **0**、跳过 **0**，**29m 29s** |
| Naming / Skills / Governance | **23/23** / **52** 项契约 / **11/11** |
| `git diff --check` | 通过 |

## 4. 状态与剩余风险

- 当前声明门槛为 Unit/Compatibility/Architecture/Integration **378/7/49/184**。
- `KnownProxies`/`KnownNetworks` 必须登记 API 连接层实际看到的代理来源，而不是浏览器或公网客户端地址。
- 容器网络、Service Mesh 或双栈切换可能改变连接层源地址；实际部署必须以 API 观测结果核对，不能复制示例后直接宣称完成。
- 本次没有数据库结构、公共 API、JSON、权限码或稳定错误码变化。

## 5. 审查、规则与 Skills 复盘

- 安全审查首轮发现 mapped IPv4 全网 CIDR、Origin 测试白名单假阳性和双栈
  回归缺口；修复后二轮审查为 Critical **0**、Important **0**，仅有两处文档
  Minor，已修正。
- 规则：高风险边界达到一次即升级门槛，已新增
  `R-20260726-trusted-proxy-boundary`，并由 Unit、Architecture 与双库
  Integration 自动化验证。
- Skills：本次是单一宿主安全边界，未形成跨模块重复且稳定的新判断型工作流；
  无需新增或实质修改项目 Skill。`delivery-map.md` 只机械同步测试门槛。
