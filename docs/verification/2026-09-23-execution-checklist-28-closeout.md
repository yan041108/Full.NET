# 执行清单 28 — 服务器监控 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **28**（实例目录、运行时/内存/CPU/版本只读信息；不暴露连接串与环境变量）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET /api/v1/observability/server-instances`、`…/{instanceKey}/runtime` |
| 服务 | `ServerMonitorService`、`ServerRuntimeReader` |
| 权限 | `observability.server.read` |
| Vue | `ObservabilityServerMonitorView.vue`（`#/observability/server-monitor`） |
| 关联 | 集群健康见 Jobs `host-health`（清单 B1/Jobs 切片，非 28 主交付） |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`ServerMonitorServiceTests` | 实例目录与 catalog-only 远程项 |
| real-stack | `host-observability-server-monitor.spec.mjs`（API + Vue + 403） |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
