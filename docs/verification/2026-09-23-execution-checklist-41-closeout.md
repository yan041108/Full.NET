# 执行清单 41 — 工作台概览 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **41**（Host 工作台时间趋势、权限裁剪指标、工作流待办/实例入口聚合；经 `IHostDashboard*` 只读 Port 批量读取）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET /api/v1/platform/host-dashboard-summary` |
| 服务 | `HostDashboardQueryService`（Identity） |
| 端口 | `IHostDashboardTenantMetricsReader`、`IHostDashboardAuditMetricsReader`、`IHostDashboardAuditTrendReader`、`IHostDashboardWorkflowEntryReader` |
| Vue | `OverviewView.vue`（指标网格、`traffic-chart`、待办入口） |
| 集成 | `PlatformHostDashboardAssertions` |
| 单元 | `HostDashboardQueryServiceTests` |
| 契约 | `platform-host-dashboard-v1.json`、`platform-dashboard.test.ts` |

## DA01 核验结论

- **已有**：12 小时访问趋势桶、今日请求/异常率、最近活动、工作流 `businessEntries`；无权限的指标为 `null` 而非伪造零值；需 `platform.dashboard.read`。
- **本槽**：补强 real-stack `host-dashboard-overview.spec.mjs`；趋势聚合以集成断言与 `HostDashboardQueryServiceTests` 为源码证据。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostDashboardQueryServiceTests` | **3/3** |
| `pnpm exec vitest run` …`OverviewView.test.ts` + `platform-dashboard.test.ts` | **6/6** |
| real-stack | `host-dashboard-overview.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
