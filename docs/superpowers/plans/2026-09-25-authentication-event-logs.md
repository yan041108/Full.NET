# Full.NET 认证事件日志开发计划

> 执行方式：遵循仓库 AGENTS.md，按任务顺序实施；不自动派发代理或创建工作树。

**目标：** 复用 Identity 已有认证审计，补齐事件覆盖、受保护查询、Vue 页面及保留运维，使管理员能够追溯登录、退出和身份安全变更。

**架构：** Identity 持有 `fn_identity_auth_audit` 及写入、查询、清理职责。后台入口位于“运维与安全 → 审计日志 → 认证事件日志”；Auditing 不跨模块读取或复制该表。认证审计不进入访问日志 B2 队列，也不使用 Outbox。

**技术栈：** .NET 10、自有 Dapper 执行/事务边界、SQL Server/MySQL、System.Text.Json 源生成、Vue 管理端、既有 CI 与 Native AOT 门禁。

## 1. 授权与当前状态

- 日期：2026-09-25；依据用户“加认证事件日志”及随后“执行”，现已开始分批实施本计划；完成状态以本文件第 10 节及实际验证为准。
- 核对基线：任务快照 `authentication-event-logs-implementation` 与当前工作区；共享工作区中其他任务的改动不作为本能力验收证据。
- 设计依据：[Identity 规格 §15](../specs/2026-07-17-identity-session-foundation-design.md#15-认证事件日志2026-09-25-规划增补)。本文件是认证事件日志的唯一活动计划；SSO 与底座总计划仅链接，不复制任务状态。
- 纠正此前分析：不能说“没有登录/退出日志”。`Domain/AuthAuditEvent.cs`、`Persistence/IdentitySql.cs` 已定义模型和 `fn_identity_auth_audit` 写入；Login、Logout、RefreshSession、ChangePassword、OAuthFlow、在线会话撤销、锁定解除及 OIDC 管理已有不同范围的写入。
- `Features/Login/Handler.cs` 已覆盖成功、未知账号、密码错误、停用、锁定等结果；`Features/Logout/Handler.cs` 在找到刷新会话后撤销 family 并写 `logout`，未知会话直接幂等成功。不能把未产生记录的重复退出宣称为新的真实退出。
- 现有字段包含用户、刷新会话、用户名指纹、事件类型、结果码、成功标志、IP、User-Agent、活动租户和时间。IP 当前为截断后的地址，不等于访问日志的 IP 指纹。
- 缺口：统一事件目录与覆盖证据、面向管理员的认证日志列表/详情、细粒度可见性、OIDC 中心与应用会话关联、专用保留策略。中心协议、MFA、恢复等路径须逐项核对，不能仅凭管理操作有审计就视为完整覆盖。

## 2. 交付范围及事件验收矩阵

| 事件组 | 最小范围 | 必须区分的语义 |
| --- | --- | --- |
| 凭据登录 | 本地账号、已有 LDAP/OAuth 入口及 OIDC 中心登录成功/失败 | 未知用户、密码错误、停用、锁定；对外保持统一拒绝，对授权管理员展示内部结果码 |
| MFA | 挑战、验证成功/失败、恢复码消费、绑定/解绑 | 挑战开始不等于登录成功；禁止记录验证码、TOTP 秘钥或恢复码 |
| 会话 | refresh、refresh 重放拒绝、主动退出、管理员撤销 | 旧刷新会话、中心会话、应用会话分别关联；浏览器关闭/自然过期不冒充主动退出 |
| SSO | 中心已有会话复用、应用会话建立、应用退出、中心退出 | 应用退出不自动标记为全局退出；中心撤销与下游登出通知成功分别表达 |
| 凭据与账号安全 | 修改密码、重置密码、恢复完成、锁定/解除锁定 | 操作者与目标用户分开；记录状态真正生效的结果，发送重置邮件不等于密码已重置 |
| 管理安全事件 | 已有客户端/密钥/授权撤销、超管等审计 | 共用存储但使用独立事件分类；登录默认筛选不混入普通管理事件 |

未知或未落地的认证方式不创建虚构成功事件。所有新增事件使用稳定机器码与资源翻译；保留历史 `login`、`logout`、`refresh` 和原始结果码，避免破坏存量数据及消费者。

## 3. AE01：确认覆盖和数据契约（P0，其他任务前置）

**文件：** `src/Modules/Full.NET.Modules.Identity/Domain/AuthAuditEvent.cs`、`Persistence/IdentitySql.cs`、`Persistence/IdentityDapperAotMaterializerContributor.cs`；新增 `Domain/AuthenticationEventCatalog.cs`；本计划的事件矩阵。

- [ ] 沿 Login、Logout、RefreshSession、ChangePassword、OAuthFlow、OIDC 中心登录/退出、MFA、密码恢复及管理撤销入口登记“入口 → 状态变更 → 写入 → 事务结果”，标出缺失和重复采集。
- [ ] 形成静态事件目录，定义分类、稳定事件码、合法结果码、可靠性和敏感字段，不依赖反射扫描。
- [ ] 确定新增可空字段：`ActorUserId`、`TraceId`、`AuthenticationMethod`、`ClientId`、`CenterSessionId`、`ApplicationSessionId`；保留 `UserId` 为目标用户，`SessionId` 为旧刷新会话，`ContextTenantId` 为发生时可信上下文。不存 Token、Cookie 或授权码。
- [ ] 为字段长度、旧事件映射和允许分类建立验证；区分旧记录未知值与新事件明确值，不回填猜测身份。

**退出条件：** 每个范围内事件都有写入位置或明确待补任务；认证拒绝、重试和事务回滚的存留语义已列明。

## 4. AE02：兼容迁移与统一写入（P0，依赖 AE01）

**文件：** 现有模型/SQL/AOT 映射；新增 Identity 内部 `Security/AuthenticationEventWriter.cs`；双库 Migrator 迁移目录中的下一对可用版本（执行时查最大编号，禁止预占冲突编号）。

- [ ] 添加可空关联列，按时间/用户查询计划添加必要索引；保留旧表与旧事件。SQL Server/MySQL 成对扩展，主键保持应用端 UUID v7。
- [ ] 封装内部写入与字段裁剪/脱敏，复用调用方事务，不在跨模块服务中直接写 Identity 表；不建立审计到用户或会话的外键，以免清理账号破坏历史。
- [ ] 成功登录、凭据变更、实际撤销等 B0 事件与对应状态同事务提交；写入失败不对外宣称安全操作成功。
- [ ] 验证拒绝事件能够独立提交：若命令返回失败触发事务回滚，使用明确的 Identity 本地拒绝审计事务边界；限制重试、遵守认证限流，不复用 B2 丢弃队列。审计存储故障返回受控服务故障，绝不因此允许认证。
- [ ] 建立双库验证：旧版本数据可读、新列为空可读、失败状态回滚、拒绝记录保留、并发重试不重复写成功事件。回退应用时保留扩展列，不删除新审计数据。

**退出条件：** 双库具有相同提交语义，字段不泄密，AOT 参数绑定与物化可达；不能以仅调用 Writer 的单测替代事务验证。

## 5. AE03：补齐认证与 SSO 采集（P0，依赖 AE02）

**文件：** `Features/Login/Handler.cs`、`Features/Logout/Handler.cs`、`Features/RefreshSession/Handler.cs`、`Features/ChangePassword/Handler.cs`、`Features/OAuthFlow/IdentityOAuthLoginSessionService.cs`、`Oidc/IdentityOidcCenterLoginService.cs`、`Oidc/IdentityOidcSessionService.cs`、`Features/OidcSession/Endpoint.cs`；AE01 发现的 MFA/恢复/管理撤销实现。

- [ ] 先为矩阵缺口建立可失败场景，再接入公共写入；已有路径替换时避免新旧两次记录。
- [ ] 分别关联中心、应用、旧会话；为 SSO 无密码复用使用独立事件类型，不计作一次新的密码校验。
- [ ] 明确退出幂等：记录真正发生的撤销；无会话时保持幂等响应，若需要记录尝试则使用独立 attempt 类型，不能伪造用户归属。
- [ ] 验证密码错误、锁定阈值、refresh 重放、管理撤销、改密失效、MFA 失败与恢复码重复使用；协议拒绝不依赖 HTTP 状态码推断。
- [ ] 验证账号/会话状态存储故障、防伪失败与多实例重试；敏感请求不得通过错误日志、Trace 或审计参数泄漏。

**退出条件：** 矩阵中已支持的认证方式和安全操作具备可追溯记录；不将尚未支持的方式标为完成。

## 6. AE04：认证事件查询 API（P0，依赖 AE02；可与 AE03 分批交付）

**文件：** 新增 Identity `Features/ListAuthenticationEvents/`、`Features/GetAuthenticationEvent/`；修改模块端点注册、授权目录、JSON 源生成、OpenAPI 契约与 AOT 映射。

- [ ] 新增 Host 管理接口 `GET /api/v1/identity/authentication-events` 和 `GET /api/v1/identity/authentication-events/{id}`。精确权限拟定 `identity.authentication-events.view`，按现有命名门禁登记。
- [ ] 列表默认最近 24 小时，单次范围最多 31 天，默认 20 条、最多 100 条，按 `(OccurredAtUtc DESC, Id DESC)` 游标翻页；支持事件分类、结果、用户 ID、客户端和 Trace ID 等受控筛选。
- [ ] 列表/详情均要求可信 Host 上下文及权限。第一期不开放租户管理员查询；登录前未知租户的记录不能因客户端传入 tenantId 归给租户。
- [ ] 响应保留原始事件/结果码并提供显示分类；默认隐藏用户名指纹，IP 脱敏、User-Agent 安全渲染；已删用户仍显示历史 ID。禁止返回密码、Token、查询串和任意扩展 JSON。
- [ ] 验证 401/403、租户越权、详情 ID 枚举、相同时间戳游标边界及日期/长度/枚举校验；错误采用 ProblemDetails。

**退出条件：** 管理员可查历史已有记录，无权账号和租户上下文无法获取列表或详情，双库结果一致。

## 7. AE05：Vue 认证事件日志页面（P0，依赖 AE04）

**文件：** 新增 `ui/admin/src/views/AuthenticationEventsView.vue`、`ui/admin/src/api/authentication-events.ts`；更新现有路由/页面注册、菜单 Baseline、权限及多语言资源，按现有生成流程更新客户端契约。

- [ ] 在“运维与安全 → 审计日志”加入入口；页面展示时间、事件、结果、用户、认证方式、应用及脱敏来源，详情显示安全关联字段。
- [ ] 加入时间范围、事件组、成功/失败和用户筛选，使用游标上一页/下一页；保留原始结果码供排障，不把登录失败显示成应用异常。
- [ ] 根据精确权限创建入口；空数据、接口失败和无权限分别显示，禁止用空表掩盖 API 错误。
- [ ] 验证登录成功/失败、主动退出、管理员撤销后出现对应事件；验证旧记录空字段及所有支持语言的显示。

**退出条件：** 真实 API 驱动的 Vue 页面可使用，历史登录/退出可见；不修改冻结的 Layui。

## 8. AE06：保留、导出与验收（P1，依赖 AE03—AE05）

**文件：** Identity `Retention/` 新增独立认证审计清理服务与选项；查询模块导出接口、Vue 导出动作；`docs/operations/data-retention.md`。

- [ ] Worker 承担 Identity 表清理，独立开关默认关闭；建议初始保留 365 天，实际值由部署方制度确定。与 OIDC 令牌清理和 Auditing 保留选项分开。
- [ ] 清理使用有界批次、截止时间及双库并发保护，支持暂停；记录删除数量与失败指标，禁止前台批量清空按钮。
- [ ] 导出使用独立 `identity.authentication-events.export` 权限，复用时间和脱敏规则，限制行数并防 CSV/Excel 公式注入；导出本身有安全审计，不能绕过详情权限。
- [ ] 按规则 §11 选择当前候选提交的 Identity 双库、架构、Vue 真实栈、Linux Native AOT 和多实例故障验证；保留失败/跳过状态，不能以文档勾选升级 Verified。
- [ ] 更新运维说明、能力矩阵和本任务状态；输出登录/退出各场景的事件查询证据、拒绝落库与回滚证据、敏感字段检查结果。

**退出条件：** 保留/导出可控，核心 P0 已通过对应门禁；生产容量仍需独立认证。

## 9. 推荐开发顺序与停止条件

顺序：AE01 → AE02 → AE03 → AE04 → AE05 → AE06。先打通“已有登录记录可查 + 补齐实际认证入口 + Vue 可见”，再扩展导出和清理。

每项执行时按仓库规则先记录基线、建立与行为相称的失败验证、实施、运行选定检查，再更新状态；本计划不授权自动提交、推送或生产发布。

出现事务回滚导致拒绝记录丢失、租户泄漏、凭据泄漏或双库语义差异时，暂停该项合入，先修复边界。数据库/CI/原生环境不可用应记录未验证项，不降低验收条件。

## 10. 2026-09-25 实施进展

已打通第一条纵向切片：复用 `fn_identity_auth_audit`，补充可空的 Trace、认证方式、客户端和中心/应用会话字段；`ActorUserId` 实际已由历史 006 迁移创建，本次未重复新增。SQL Server/MySQL 235 迁移成对；Host 管理员可通过独立权限查询列表/详情，响应不输出用户名指纹、IP、User-Agent。Vue 认证事件页已接入生成的 OpenAPI 客户端，提供时间、用户、事件和结果筛选、受限页号分页与详情。现有登录/退出/刷新/改密写入增加部分上下文；OIDC 中心登录、凭据拒绝、应用会话建立及实际主动退出增加事件。OIDC 凭据状态/会话/审计及主动撤销/授权/审计使用 Identity 本地事务。

本轮验证：Identity API Release 构建、Vue 类型检查、迁移命名检查、SQL 安全检查和 OpenAPI 客户端生成门禁通过；新增查询集成场景在 SQL Server/MySQL 各通过一次。此证据只覆盖上述纵向切片，不代表 AE01—AE06 全部完成。

独立 Identity Worker 保留任务已实现默认关闭配置、有界双库清理及删除/失败计数；实际启用须由部署方确认制度。受控 CSV 导出已要求读取与独立导出权限，限定 31 天/1 万行，使用安全投影、公式转义并审计导出动作；双库查询集成测试覆盖该路径。普通 Host 用户读取/导出返回 403 的双库负例已覆盖。235 迁移移除认证审计到用户的历史外键，使删除用户后仍可保留审计历史；禁用 schema 模板的 SQL Server/MySQL 空库测试各通过一次，并通过元数据断言外键已不存在。剩余门禁：完整事件矩阵（MFA、密码恢复、SSO 会话复用及更多失败类型）、拒绝审计/撤销在故障与并发下的事务测试、游标分页、保留的并发/故障恢复与真实运维证据、真实浏览器和 Linux Native AOT。
