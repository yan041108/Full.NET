# 执行清单 48 — 受控注册策略与注册来源 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **48**（受控注册政策、注册方式管理、邮箱挑战注册与公开入口边界；**短信登录**不在本槽）。

## Gate C 与启动说明

- Phase D **87** 已登记 Gate C **出口未满足**（WF+DA Build-verified）。
- 在用户 **「继续」** 指令下，以 **证据 closeout** 方式启动 C 区 **48**，**不**视为 Gate C Verified 或 parity Verified 升级。

## 交付锚点

| 层 | 位置 |
|----|------|
| 策略 API | `GET/PUT /api/v1/identity/registration-policy`（`identity.registration_policy.*`） |
| 注册方式 CRUD | `/api/v1/identity/registration-ways`（`identity.registration_ways.*`） |
| 公开列表 | `GET /api/v1/identity/public/registration-ways?tenantId=`（策略关闭时 **403** `identity.registration.disabled`） |
| 匿名注册 | `POST /api/v1/identity/register` + `SendEmailChallenge`；默认 **Disabled** |
| 数据 | 迁移 147 表 + 策略种子；148 动作权限恢复测试 |
| Vue | `RegistrationWaysView`（策略开关 + 方式表）；`RegisterView`（邀请/方式参数） |
| 单元/集成 | `RegistrationWayManagementServiceTests`；`RegistrationTransactionBoundaryTests`；`Migration147*` / `Migration148*` |

## 清单 48 验收结论

- **已有**：三态策略（Disabled / InvitationOnly / Open）、租户级注册方式（默认角色/机构/职位）、邮箱挑战闭环；默认不开放公网注册。
- **本槽**：`phase-c-48-registration-policy-ways.spec.mjs`（策略默认值、公开列表与 register fail-closed；管理页冒烟）。
- **未做（刻意）**：短信登录、手机验证码注册、Captcha 公网产品入口；45 的 `sms.aliyun` 仅通知渠道，不接线登录。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`RegistrationWayManagementServiceTests`…`RegistrationTransactionBoundaryTests` | **11/11**（`d40de4e6`） |
| real-stack | `phase-c-48-registration-policy-ways.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 CI fresh、real-stack 绿与 Gate C 决策约束。
