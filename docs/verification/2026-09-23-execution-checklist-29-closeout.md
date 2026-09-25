# 执行清单 29 — 缓存策略控制面 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **29**（已登记缓存命名空间/策略只读查看与精确失效；无 SCAN/全库清空）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET/POST …/observability/cache-policies`、`…/{entryName}/invalidations` |
| 服务 | `CachePolicyControlPlane` |
| 权限 | `observability.cache_policies.read`、`observability.cache_policies.invalidate` |
| Vue | `ObservabilityCachePoliciesView.vue` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`CachePolicyControlPlaneTests` | 登记目录与失效边界 |
| real-stack | `host-observability-cache-policies.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
