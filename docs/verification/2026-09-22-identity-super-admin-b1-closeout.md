# B1-1 Identity 超级管理员 Task 5 收口（2026-09-22）

## 范围

关闭 [super-administrator Task 5](../superpowers/plans/2026-07-18-super-administrator.md) 与 [Phase B1 kickoff](./2026-09-22-phase-b1-identity-kickoff.md) 中仍开放的 **Vue 真实栈授撤** 与 **Production TOTP Playwright 入口**；不升 Identity 整体为 `Verified`（仍缺完整双库 CI fresh 与 Linux 原生证据）。

## 交付

| 项 | 变更 |
| --- | --- |
| Real-stack UI 授撤 | [`host-super-administrators.spec.mjs`](../../tests/e2e/admin-real-stack/tests/host-super-administrators.spec.mjs)：对话框授予/撤销；最后一名保护 UI 展示 `identity.super_administrator.last_remaining` |
| Production TOTP | [`host-super-administrators-production-totp.spec.mjs`](../../tests/e2e/admin-real-stack/tests/host-super-administrators-production-totp.spec.mjs) + [`run-production-totp-e2e.mjs`](../../tests/e2e/admin-real-stack/scripts/run-production-totp-e2e.mjs)；根脚本 `pnpm test:e2e:real:production-totp` |
| WCAG | admin-parity「超级管理员页」axe 用例；[`SuperAdministratorsView.vue`](../../ui/admin/src/views/SuperAdministratorsView.vue)「最后一名受保护」标签对比度修正 |
| OpenAPI | 既有 [`identity-super-administrators-v1.json`](../../contracts/openapi/identity-super-administrators-v1.json) 已含 `totpCode`，无契约变更 |

## 推荐验证命令

| 门禁 | 命令 |
| --- | --- |
| OpenAPI / 治理 | `pnpm test:openapi`、`pnpm test:governance` |
| Super-admin 契约 | `node --test tests/openapi/identity-super-administrators-contract.test.mjs` |
| 单元 | `dotnet test tests/Full.NET.UnitTests --filter SuperAdministrator` |
| admin-parity WCAG | `pnpm test:e2e:admin -- --grep "超级管理员页"` |
| Real-stack（需 Docker/Testcontainers） | `pnpm test:e2e:real -- --grep super-admin` |
| Real-stack MySQL | `pnpm test:e2e:real:mysql -- --grep super-admin` |
| Production TOTP 栈 | `pnpm test:e2e:real:production-totp`（`FULLNET_E2E_STACK_PROFILE=production-totp` 引导） |

## 本环境执行结果（2026-09-22）

| 命令 | 结果 |
| --- | --- |
| `node --test tests/openapi/identity-super-administrators-contract.test.mjs` | 通过 |
| `pnpm test:governance` | 54/54 通过 |
| `pnpm test:e2e:admin -- --grep "超级管理员页"` | 通过（axe 2.2 A/AA） |
| `dotnet test tests/Full.NET.UnitTests --filter SuperAdministrator` | 见当次 CI/本地构建（曾因 Document 契约测试构造函数参数未对齐而阻塞，已补齐 `IIdGenerator` 占位） |
| `pnpm test:e2e:real -- --grep super-admin` | **未执行**（`Could not find a working container runtime strategy`） |
| `pnpm test:e2e:real:production-totp` | **未执行**（同上，需 Docker/Testcontainers） |

双库 real-stack 与 Production TOTP profile 须在容器就绪的 CI 或开发机上复跑后再记入 [identity-super-admin-real-stack-2026-07-21.md](./identity-super-admin-real-stack-2026-07-21.md) §仍开放。

## 仍开放

- Identity 用户管理缺口（执行清单 ID03–ID09）、F05/F08 席位编排（foundation 切片）。
- Production TOTP 纳入 main CI matrix（当前为独立脚本 + 条件 skip）。
- 双库 fresh 全绿后，再讨论 super-admin / Identity 行升 **Verified**。
