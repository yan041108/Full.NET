# Cursor Admin.NET 吸收代码审查记录（2026-08-06）

## 1. 审查范围

- 基线提交：`1692558994921a65ec48a913727eaf3df4a2d923`
- 任务快照：`cursor-adminnet-review-20260806`
- 已审查 Cursor 提交：Document 上传/下载、Jobs 计划页、用户/角色 Art Design 改造及批量 CRUD 页面改造。
- 已审查未提交工作区：Identity 用户档案迁移 `082`、Host 菜单目录同步、自定义菜单、用户组织关系聚合接口及对应 Vue 页面。
- 产品边界：只审查 Vue 主管理端；`ui/admin-layui/**` 继续冻结。

本次结论是：现有代码已经具备较多可用页面和后端切片，但当前工作区仍不是可合并候选。静态、Unit 和前端问题已修正；`082`、自定义菜单和组织关系仍缺 SQL Server/MySQL 新鲜验证，且存在必须继续收口的字段授权和操作权限设计。

## 2. 本轮已纠正的问题

| 级别 | 问题 | 纠正结果 |
| --- | --- | --- |
| Critical | 租户切换后快照读取失败时恢复旧 Token，使客户端仍表现为已认证 | 恢复失败关闭：清空会话和持久化凭据；回归测试覆盖旧凭据不得恢复。 |
| Critical | 用户档案包含证件号、联系方式、住址和紧急联系人等敏感字段，却在 `identity.users.read` 下无条件返回；普通管理员编辑还会把不可见字段覆盖为 `null` | 临时安全边界改为只有且仅有一个显式超级管理员 Claim 时读取/写入档案；普通管理员响应不查询档案、请求携带档案返回 `403`，Vue 不创建档案入口且不提交档案。最终字段级授权仍列为后续 P0。 |
| Important | Organization 直接查询 Tenancy 物理表，违反模块边界并新增未登记 Global SQL | 新增窄接口 `IActiveTenantContextResolver`，由 Tenancy 实现和注册，Organization 只消费抽象；移除跨模块 SQL。Architecture 恢复通过。 |
| Important | 自定义 Host 菜单更新缺少后代父节点环校验 | 复用服务端父环校验，并增加 Integration 回归断言。 |
| Important | 用户机构关系重新激活时先清除其他主机构；若重新激活影响行数为零，事务会以业务失败结果提交前置修改 | 先以 affected-row 不变量完成重新激活，再清理其他主机构；零行返回已存在，多行抛出并回滚。 |
| Important | Jobs Cron 预览吞掉取消并捕获所有异常 | RED 证明取消被吞；GREEN 后先传播取消，只把 Cron、时区和不可达下一次执行等预期输入异常映射为校验失败。 |
| Important | Vue 批量改造产生类型错误，Unit 新测试方法不完整，种子 Contributor 注册断言过期 | 修复表格 slot 类型、Art Search/Tabs/Transfer 类型、完整恢复 Navigation 测试，并同步 Contributor 断言。 |
| Governance | Unit 和 migration partition 门槛落后于实际发现数 | Unit 最低值更新为 `1107`；migration full partition 最低值更新为 `490`。最终合并前仍须以 fresh discovery 再确认。 |

## 3. 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm --filter @fullnet/admin typecheck` | PASS |
| `pnpm --filter @fullnet/admin test` | 115 files / 396 tests PASS |
| `pnpm --filter @fullnet/client-contracts test` | 47 files / 124 tests PASS |
| `dotnet build Full.NET.slnx --configuration Release --no-restore` | 0 warning / 0 error |
| `dotnet test tests/Full.NET.UnitTests/... -c Release --no-build --no-restore` | 1107/1107 PASS（此前已 fresh build） |
| `pnpm test:naming` | 24/24 PASS |
| `pnpm test:sql-safety` | 5/5 PASS |
| `pnpm test:openapi` | 73/73 PASS |
| `pnpm test:governance` | 17/17 PASS（修正门槛后） |
| `dotnet test tests/Full.NET.ArchitectureTests/... -c Release` | 78/78 PASS（移除跨模块表访问后） |
| `HostUserFieldProjectionTests` + `HostJobScheduleServiceTests` | 15/15 PASS |
| `HostJobScheduleServiceTests` RED 证据 | 9/10，唯一失败为取消未传播；实现修复前用于证明缺陷 |
| `pnpm test:integration:partitions` | 490 项无遗漏或重复：52 / 52 / 280 / 106 |
| SQL Server/MySQL affected Integration | PASS：`pnpm test:integration:affected -- --snapshot cursor-adminnet-wip-stabilization-20260806 --phase inner` 通过 `smoke 8/8` + `Identity 78/78`；`--phase slice` 通过 `smoke 8/8` + `Identity, Organization 88/88` |

最终交付前必须重跑完整 Unit、Vue、Architecture、governance 和同一快照 affected slice。本记录中的局部通过不能替代双 Provider 与真实栈验收。

### 3.1 Task 0C 续跑更新（2026-08-06 晚）

- Host 用户组织窄接口已补齐职位隶属 `update/disable` Endpoint，并在聚合参考读取中按 `organization.user_units.*` / `organization.user_positions.*` 精确权限做失败关闭与结果投影，不再把组织参考访问等同于 `identity.users.read` 的页面读取。
- Vue `UsersView` 已接入 Host 侧职位隶属 `update/disable` API；编辑用户时若切换主职位，会按现有权限执行设主职位与清理旧隶属，避免前端只能新增职位而无法收口旧关系。
- 新增 `ui/admin/src/api/host-user-organization-reference.test.ts`，并补充 `UsersView.test.ts` 覆盖“仅授予职位禁用权限仍会加载组织参考”的权限门行为。
- 新增 `tests/Full.NET.UnitTests/Organization/HostUserManagementReferenceEndpointTests.cs`，通过反射锁定 `HostUserManagementReference.Endpoint.TryResolveReferenceAccess(...)` 的组织权限判定，覆盖“无组织权限拒绝”“仅机构权限放行”“仅职位权限放行”“两侧权限同时放行”。
- 新鲜验证：
  - `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "AuthorizationCatalogTests|HostUserProfileMapperTests|HostUserFieldProjectionTests"`：39/39 PASS
  - `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "HostUserManagementReferenceEndpointTests|AuthorizationCatalogTests|IdentityModuleRegistrationTests"`：39/39 PASS
  - `pnpm --filter @fullnet/admin test -- "src/api/host-user-organization-reference.test.ts" "src/views/UsersView.test.ts" "src/views/components/UserEditorDialog.test.ts"`：18/18 PASS
  - `pnpm --filter @fullnet/admin build`：PASS
  - `pnpm test:integration:affected:plan -- --snapshot cursor-adminnet-wip-stabilization-20260806 --phase inner`：命中 `Identity, Organization`
- 本机 Docker 已恢复可用；按同一任务快照完成受影响 Integration：
  - `pnpm test:integration:affected -- --snapshot cursor-adminnet-wip-stabilization-20260806 --phase inner`：`smoke 8/8 PASS`，`Identity 78/78 PASS`
  - `pnpm test:integration:affected -- --snapshot cursor-adminnet-wip-stabilization-20260806 --phase slice`：`smoke 8/8 PASS`，`Identity, Organization 88/88 PASS`
  - 首次 `inner` 失败暴露的是 Host 菜单 OpenAPI 断言未适配最小 API 对数组响应的 schema 生成方式；修正 `OpenApiHostMenusContractAssertions` 后已通过复验。

## 4. 尚未关闭的审查结论

1. 用户档案目前只有“超级管理员可见”的临时失败关闭边界，尚未进入既有 `FieldProjectionCatalog`，也没有字段掩码或 Patch 语义；不得把它标记为 Admin.NET 字段授权等价完成。
2. `HOST_MENU_ASSIGNABLE_PERMISSIONS` 是前端硬编码的少量旧权限，已经遗漏 Document、Jobs、Notifications、Settings 等实际页面，必须改为由服务端授权目录投影的自包含选项接口。
3. 用户页新增的主机构、附属机构和职位写操作复用 `identity.users.update`，不满足“页面/按钮/后端 Endpoint 独立稳定权限码”；多请求保存还可能部分成功。
4. Document 上传当前先把 Files 对象推进 Ready，再写 Document 版本；Document 事务确定回滚或提交结果未知时缺少 claim/release 对账协议，可能永久遗留无引用 Ready Blob。
5. Jobs Cron 预览只要求 `jobs.schedules.create`，因此只有 update 权限的编辑者无法预览；应建立独立 preview 权限，或提供服务端可证明的 create/update OR 策略，禁止扩大为 read。
6. 用户/机构新接口、迁移 `082`、自定义菜单目录同步缺少本机 SQL Server/MySQL 恢复、权限拒绝和真实 Vue 栈证据。
7. 用户页将创建用户、组织关系、职位和角色拆成多个请求，任一后续步骤失败都会出现部分完成；UI 必须显示逐步骤结果并提供安全重试，或设计受控编排用例，不能显示笼统“保存失败”掩盖已提交状态。

规则演进检查：本轮命中既有逐操作授权、跨模块边界、字段授权和失败关闭规则，无新增规则缺口，不修改规则候选。
