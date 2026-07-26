# OpenAPI 破坏性变更门禁验证（2026-07-27）

- 状态：已合入 `main`，隔离分支、工作树注册与物理残留均已清理

## 摘要

为 `contracts/openapi/*.json` 增加纯离线向后兼容比较器。Pull request 现在以
`github.event.pull_request.base.sha` 为基线，比较当前工作树中的全部冻结夹具；无需启动 API、
数据库或 Docker，也不依赖外部服务。

## 兼容边界

允许：

- 新增版本化夹具、路径、操作、schema 或属性；
- 修改说明文本；
- 调整路径、操作和属性的数组顺序。

阻止：

- 删除既有夹具、路径、HTTP 方法、schema 或属性；
- 改变既有操作的权限码、成功状态码、请求 schema 或响应 schema；
- 改变分页/集合 schema 的 `itemSchema`；
- 改变 OpenAPI JSON、Scalar UI 与安全方案等平台稳定配置；
- 通过修改同一 v1 文件中的 `id` 或 `version` 绕过兼容门禁。

若需要破坏性演进，必须保留原 v1 夹具并新增版本化夹具；本门禁不把同文件版本字段递增视为豁免。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 行为 RED | CLI 尚不存在时，兼容变化、破坏变化、Git ref 和错误退出码聚焦 **0/7** |
| 行为 GREEN | 目录比较、稳定诊断与 Git ref 加载聚焦 **7/7** |
| CI RED | `package.json` 缺少入口，PR workflow 未传 base SHA，聚焦 **0/1** |
| CI GREEN | package/checkout 完整历史/PR base SHA wiring **1/1** |
| 真实基线 | `pnpm test:openapi:breaking -- --base-ref HEAD` 比较 **25/25** 个夹具，无破坏变化 |

OpenAPI 离线测试发现数由 **50 → 58**；.NET canonical 门槛保持
**390/7/49/186**，因为本切片未修改 C#、数据库或 Integration 测试。

## 实现与 CI

- 纯比较器：`scripts/openapi/openapi-contract-compatibility.mjs`
- CLI：`scripts/openapi/check-openapi-breaking-changes.mjs`
- 本地入口：`pnpm test:openapi:breaking -- --base-ref <git-ref>`
- PR 基线：`${{ github.event.pull_request.base.sha }}`
- 诊断按英文稳定键排序，包含夹具文件、路径/方法或 schema 精确位置。

## 完整非 Docker 验证

| 命令 | 结果 |
| --- | --- |
| `pnpm test:openapi` | **58/58**，失败 0、跳过 0 |
| `pnpm test:openapi:breaking -- --base-ref HEAD` | **25/25** 个夹具兼容 |
| `pnpm test:governance` | **11/11** |
| `pnpm test:skills` | `fullnet-module-delivery` **52** 项合同检查 |
| `pnpm test:workspace` | 退出码 0 |
| `git diff --check` | 退出码 0 |

本切片不占用 Docker；Realtime 双库全量测试可在另一隔离工作树持续执行。

## 规则与 Skill 复盘

- 既有规则已经禁止静默更改公共 API，并要求版本化或兼容接受旧值；本次没有发现新的规则歧义，
  因此不新增或修改 `AGENTS.md` / `rules/development-quality.md` / `rules/naming-conventions.md`。
- `fullnet-api-compatibility` 候选由 **4 → 5**，但本轮缺口已经由确定性脚本、测试和 CI 完整覆盖，
  按 `rules/skill-evolution.md` 的自动化优先原则不创建新 Skill。
- 首个多客户端生成或真实 SDK 消费者落地后，再评估剩余人工判断是否形成稳定、项目特有的 Skill。
