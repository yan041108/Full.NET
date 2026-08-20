# SerialNumbers Build-verified → Verified 切片验证清单

- 日期：2026-08-20
- 快照：`serialnumbers-verified-slice-20260820`
- 基线：`main@e008f64e`
- 范围：Host 流水号规则列表筛选/稳定排序、Vue 服务端分页与表单边界提示、精确权限门控、真实栈 E2E 规格扩展
- **Verified 升档**：否（须双库 Integration + 真实栈 fresh 全绿后再改 `adminnet-feature-parity.md`）

## 本切片交付

| 项 | 说明 |
| --- | --- |
| List API | `name` / `key` / `isEnabled` 筛选；`sortBy` / `sortDirection` 白名单排序，末尾固定 `Id ASC` |
| Vue | 服务端分页、筛选栏、Pattern/UTC 重置/数值边界/语义冻结提示；`PermissionGate` 覆盖 create/update/enable/disable/preview |
| E2E | `serial-number-rules.spec.mjs` 覆盖创建/更新/预览与 read-only 403 |
| Layui | 未修改 |

## 建议执行的测试

工作区已脏，统一使用同一快照：

```bash
pnpm test:inner -- --snapshot serialnumbers-verified-slice-20260820 --plan
pnpm test:inner -- --snapshot serialnumbers-verified-slice-20260820

# 纵向关闭前（含双库 Integration 影响集）
pnpm test:slice -- --snapshot serialnumbers-verified-slice-20260820
# 或
pnpm test:integration:affected -- --snapshot serialnumbers-verified-slice-20260820 --phase slice
```

聚焦回归时可直接跑：

```bash
# Unit：排序白名单
dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~SerialNumberRuleListOrderByTests

# Integration：双库规则 API（含筛选断言）
dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~SerialNumbersApi

# Vue 组件
pnpm --filter @fullnet/admin exec vitest run src/views/SerialNumberRulesView.test.ts

# 真实栈（需 Docker / API 已起；双库分别跑）
pnpm test:e2e:real -- --grep "流水号"
pnpm test:e2e:real:mysql -- --grep "流水号"
```

## 本窗口已执行

| 门禁 | 结果 |
| --- | --- |
| SerialNumbers 模块 Release/Debug 编译 | **通过** |
| Unit `FullyQualifiedName~SerialNumber`（含 `SerialNumberRuleListOrderByTests`） | **9/9** |
| Vue `SerialNumberRulesView.test.ts` | **7/7** |
| `git diff --check`（本切片路径） | **通过**（仅 CRLF 提示） |
| Integration `SerialNumbersApi*` 双库 | **未执行** — 工作区无关的 `Full.NET.Messaging.Kafka` 编译错误阻断 IntegrationTests 构建 |
| `pnpm test:e2e:real` / MySQL 真实栈 | **未执行** |

因此 **未** 将 `adminnet-feature-parity.md` 升档为 Verified。

## 升档条件

仅当下列全部有新鲜通过输出后，才可将 `docs/roadmap/adminnet-feature-parity.md` 中「流水号规则」从 **Build-verified** 改为 **Verified**：

1. SQL Server + MySQL Integration `SerialNumbersApi*` 通过（含列表筛选）
2. Vue `SerialNumberRulesView` 单元测试通过
3. `admin-real-stack` 双库聚焦 `serial-number-rules.spec.mjs` 通过

## 规则与 Skill

未命中规则/Skill 演进触发条件；未修改 `rules/` 或 `.agents/skills/`。
