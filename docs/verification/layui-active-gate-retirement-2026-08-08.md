# Layui 活动交付门禁退役（2026-08-08）

**任务快照：** `cursor-review-layui-freeze-ci-20260808`  
**基线提交：** `abb02d69872c0340f1dc1ca3371c200d032d5c3b`（Task 3 完成后）  
**范围：** 默认 CI/脚本/E2E/包体预算不再选择 `@fullnet/admin-layui`；冻结扫描与显式 `test:e2e:layui-frozen` 保留。

## 变更摘要

| 区域 | 行为 |
|------|------|
| `package.json` | `test:clients` / `build:clients` 显式 `--filter=!@fullnet/admin-layui`；`test:e2e` → `test:e2e:admin`；新增 `test:e2e:layui-frozen` |
| `.github/workflows/ci.yml` | 客户端测试不再包含 Layui；E2E 使用 `pnpm test:e2e:admin` |
| `tests/e2e/admin-parity/` | 活动配置仅 `vue-admin`；Layui 迁至 `playwright.layui-frozen.config.mjs` |
| `tests/e2e/admin-real-stack/` | 移除 Layui webServer/project |
| `tests/performance/frontend-bundle-budgets.json` | 移除 Layui dist 预算目标 |
| 治理测试 | `client-workspace.test.mjs`、`layui-freeze.test.mjs` 断言默认门禁排除 Layui |

**不变量：** `ui/admin-layui/**` 零功能 diff；`layui-freeze.test.mjs` 冻结基线扫描继续失败关闭。

## 验证（2026-08-08 本机）

| 命令 | 结果 |
|------|------|
| `node --test tests/client-workspace.test.mjs tests/governance/layui-freeze.test.mjs` | **3/3** 通过 |
| `pnpm test:governance` | **27/27** 通过 |
| `pnpm --filter @fullnet/admin test` | **406/406** 通过（修复 `host-user-organization-reference.ts` UTF-16 损坏后） |
| `pnpm --filter @fullnet/admin build` | 通过（顺带补全 `diagnostic-policy.ts` 类型再导出） |
| `pnpm test:e2e:admin` | **13 通过 / 34 失败 / 4 跳过**（约 6.1 分钟） |
| `git diff --check` | 通过（仅 CRLF 提示） |

### E2E 说明

失败集中在 `shell-parity.spec.mjs`：Art Design 壳层侧栏为 `complementary` + `menubar`，测试仍查找 `navigation` + `link`，与 Layui 双端 parity 时代的选择器不一致。此为 **Vue 活动套件选择器漂移**，不是 Layui 门禁退役回归。后续应在独立任务中将 `admin-parity` 活动套件迁移为 Vue 原生 ARIA 语义（或拆分更小的 vue-active smoke）。

`test:e2e:layui-frozen` 未在本机执行（需显式授权维护 Layui 时运行）。

## 附带修复（非 Layui 门禁本体）

- `packages/client-contracts/src/host-user-organization-reference.ts`：Task 2 遗留 UTF-16 损坏，阻塞 Vitest。
- `ui/admin/src/api/diagnostic-policy.ts`：补 `DiagnosticPolicy` 类型再导出，修复 `vue-tsc` 构建。

## 规则复盘

未命中 `rule-evolution` 升级条件；未修改 Skill。