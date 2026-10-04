# 从模板到首个 CRUD（入口与验收边界）

1. 组装并创建应用（可复制的 `Demo` / `acme` Minimal 命令见[首个 CRUD 教程](create-first-crud.md#从空目录创建示例应用)；其他参数见 [`templates/fullnet-app/README.md`](../../templates/fullnet-app/README.md)）。
2. 诊断：`pnpm run diagnose:development`（应用根目录）。
3. 通过进程环境或受控秘密配置提供数据库连接。可销毁空库演练须按教程[第六步](create-first-crud.md#第六步显式接入业务迁移)接入应用业务 SQL、编译 Migrator，并显式提供开发签名与框架维护门禁；只有连接和 Development 环境不足以启动该冻结预设的迁移。默认只迁移；后续本地账号演练先提供 `Identity__Bootstrap__Username` 与满足强密码规则的 `Identity__Bootstrap__Password`，再显式选择 `-Seed development`，不将秘密写进文档或 Git。Production 只允许 Baseline，使用自己的签名密钥和批准的维护/恢复流程。旧应用需显式采用新模板的应用 Migrator，源码升级不会覆盖或创建应用拥有的入口。
4. 使用 [`Full.NET.CodeGeneration.Cli`](../../src/Tools/Full.NET.CodeGeneration.Cli/) 在应用工作区生成租户 CRUD（表名遵循 `{owner}_*` 命名，见 [`samples/enterprise-request`](../../samples/enterprise-request)）。模板 API、Worker 与 Migrator 通过 `src/<name>.Composition/ApplicationModuleCatalog.cs` 按各自 Profile 装配应用模块；接入目标使用该应用自有项目和标准 `CreateModules()` 清单，不修改 `framework/fullnet/` 的受管官方目录。Worker 编译随应用分发的框架后台处理管线，并使用应用目录注册业务后台能力；Minimal 预设已纳入双库启动、健康检查、空 Outbox 版本扫描及一条合法 Tenancy 事件的首次领取与成功终态验收。其他事件路由、失败重试、应用自有 Native AOT 尚待验证。默认 Runner 只执行预设框架迁移；第六步通过应用自有包装器显式执行一对编号业务脚本，不自动采纳草稿或修复任意已有表。
5. 二次生成应保留未登记的人工文件；人工修改的受管 Handler/SQL 应触发冲突并拒绝覆盖，不能把自动保留误解为自动合并。代表性生成 CRUD 与教程子项均已按本地真实证据关闭；教程[第五步](create-first-crud.md#第五步模块接入可选)完成模块/API、业务客户端和 Vue 接线/构建及再生成保护，[第六步](create-first-crud.md#第六步显式接入业务迁移)完成双库首次/重复迁移与未记账恢复，[第七步](create-first-crud.md#第七步开发播种与启动-api)完成双库开发播种、API/运行契约、五类普通账号浏览器与租户隔离。证据来自同一冻结应用的分段实走，各阶段数据库独立；不能理解为本轮重跑了完整创建器套件。F02 完整配置诊断仍待收口，教程应用 Worker、Native AOT 和容量仍未验证。

原框架仓库的快速验证使用矩阵包装器与最低发现门禁；模板真实双库验收可在本地执行，入口与范围见[本地真实链路验收](create-first-crud.md#本地真实链路验收)，GitHub Actions 继续提供回归证据。独立应用不含原仓库全部测试项目，不能在应用目录照抄 `tests/Full.NET.UnitTests` 命令。

企业申请样例的创建入口通过 `X-FullNet-Organization-Unit-Id` 传递机构选择，服务端按当前租户与用户校验组织权限；JSON 只包含业务可写字段，创建/修改/删除操作人由服务端确定。浏览器验收入口为 `enterprise-request.spec.mjs --project vue-admin`，实际租户 CRUD 与 API 的审批、CSV 导入验收分别记录。

生成客户端读取器会按 OpenAPI 明确声明的整数 JSON 编码，将合法整数字符串归一为既有 TypeScript `number`，并拒绝超出安全整数范围的值；不会转换金额字符串或其他普通文本。嵌套引用、数组和联合分支使用相同边界。需要超过 `Number.MAX_SAFE_INTEGER` 的业务值时，应另行定义显式字符串契约，不能静默截断精度。
