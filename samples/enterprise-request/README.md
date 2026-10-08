# Enterprise Request 样例

主从（header + line）租户单据样板，配合 Full.NET CRUD 生成器演示 F09 业务接入标准。

## 内容

- `schema.json` — `master.detail` CRUD Schema（申请头 `enterprise_request` + 明细 `enterprise_request_line`）
- `integration-target.json` — 模块/Composition/Vue 路由接入目标
- `src/` — 最小模块骨架（`EnterpriseRequestModule.cs`）
- `tests/schema.test.mjs` — Schema 结构契约测试

## 快速开始

```bash
# 预览生成
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- \
  --schema samples/enterprise-request/schema.json --workspace .

# 规划接入
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema samples/enterprise-request/schema.json \
  --repository . \
  --target samples/enterprise-request/integration-target.json
```

## E2E

管理端路由 `/enterprise-requests` 由 `tests/e2e/admin-real-stack/tests/enterprise-request.spec.mjs` 桩测试覆盖。

## 静态工作簿导入

使用 `demo.enterprise_requests` Schema 的 `requests` 工作表，从正式模板下载入口获取 `.xlsx`；不接受改名为 `.xlsx` 的 CSV。固定列为 `requestNumber`、`title`、`totalAmount`、`applicantUserId`、`organizationUnitId`。金额使用不带千位分隔符的 invariant 十进制，必须精确落入数据库 decimal(18,2)，机构与申请人使用非空 Guid。

样例限制上传 1 MiB、压缩目录声明的解压总量 4 MiB、32 个压缩条目、单个 XML 2 MiB 字符和 1,000 个数据行；拒绝公式、外部关系、重复或错位单元格以及漂移表头。空白行跳过，字段错误保留 Excel 原始行号；预校验不创建业务实体。

执行检查点是有效行的零基序号，回执身份为可信租户、任务标识与原始行号。申请创建、回执占位与完成在同一事务内，复用生成的领域创建和组织写授权。相同载荷重放返回同一实体，并再次检查实体当前机构的写授权；不同载荷或不完整回执失败关闭。迁移 244 只增加样例自有回执表，不修改原有业务数据、不建立跨模块外键；回退应用时可保留该表，迁移重跑保留已提交回执。

API 和 Worker 使用相同处理器及最小后台授权依赖。执行、恢复和重试只接受任务创建人的当前交互会话；Worker 每批通过 Identity Port 重查会话、精确执行权限及 Schema 权限，再读取文件和调用业务处理器。原预览能力保持不变，撤销已授予能力后必须先恢复权限；新授予能力不能改变原有效行集合。检查点尚未完成却返回空批会明确失败，避免持续无进展排队。

升级时先停止并排空旧 Worker，再切换新版 API/Worker，禁止旧执行器与新版队列混跑。旧队列没有会话绑定时拒绝直接执行，由原创建人的有效会话显式重试；旧任务缺少能力快照且 Schema 声明附加能力时必须重新上传预览。不接受 API Key 替代后台会话委托，也不支持更换用户恢复同一任务。

错误回执下载也要求原创建人的当前交互会话，并权威复核执行权限、Schema 权限与原预览已授予的能力；原会话撤销后不能继续下载，同一创建人的新会话可重新授权。此下载校验不重新执行业务导入。

正式 Worker profile 的后台宿主恢复验证与独立 Worker 操作系统进程崩溃验证需分别记录；样例生成 CRUD 的完整 Native AOT 运行仍需独立验收。

导入 HTTP 入口通过 Identity 权限解释器冻结当前作用域的有效能力，兼容超级管理员令牌不携带逐项权限 Claim 的情况；组织单位/职级附加能力仍须逐项核对，Host-only 能力不能进入租户快照。原预览缺少能力时重新上传，不因后来授权而改变原有效行集合。样例业务 API 的金额保持 decimal 字符串，草稿状态机器码为 `Draft`。

在干净源码检出中，可启用本地双库独立应用验收（需要 Docker、.NET、Node 和 Python 标准库）：

```powershell
$env:FULLNET_RUN_TEMPLATE_REAL_STACK='1'
$env:FULLNET_TESTCONTAINERS_REUSE='0'
node --test tests/templates/created-enterprise-data-delivery.test.mjs
```

该测试从固定提交生成新的 Enterprise 应用，使用自己的数据库和 Redis，构建三个宿主并执行迁移重放；两张正式工作簿在 API 中排队后才启动独立 Worker，通过正式业务 API 回读单位/职级、申请人、金额与租户。报告保留在 `.tmp/template-real-stack/enterprise-delivery/`；生成应用保留在本次独占的系统临时短目录，报告 `applicationRoot` 记录其路径，以避免 Windows 深层依赖目录的 Node/Vite package-import 解析问题。只清理本次拥有的进程和容器。验收器同时执行正式报表查询、工作簿导出、租户档案与企业申请打印，并从生成应用自己的 Vue 骨架进行真实浏览器授权、预览、打印前复核和撤权验证。正常消费与输出链分别记录，不包含 Worker OS 崩溃、审批或完整 Native AOT 业务链。浏览器打印验证调用时序、内容与打印媒体样式，不验证实体打印机或系统打印对话框。


## 企业申请摘要打印

`EnterpriseRequest` 显式依赖 `Printing`，通过 `IPrintingFormSchemaContributor` 注册固定表单 `enterprise_request.request_summary`，通过 `IPrintingRecordBindingSource` 提供申请编号、标题、状态与 invariant 金额。目录贡献者只持有静态元数据；绑定源按请求作用域注册，由业务模块拥有读取权限和数据范围判断。Printing 不直接读取申请表，也不接收客户端 SQL、字段值或租户覆盖。

Host 创建此 Schema 的模板、发布并授予租户精确版本；租户在 `/printing/published-templates` 中选择获授版本，填写申请 UUID。发布目录中的 `requiresRecordId` 表明必须选择业务记录，无需访问 Host Schema 目录。预览请求示例：

```json
{"versionNumber":1,"recordId":"01980000-0000-7000-8000-000000000001"}
```

申请绑定要求当前交互会话具有 `enterprise_request.enterprise_requests.read`，并复用申请 API 的当前租户与组织数据范围；超级管理员仍受租户隔离约束。绑定前后均通过 Identity Port 复核会话与精确业务权限，Printing 在交付前复核版本授权和启用状态。没有记录编号返回验证失败，不可读取的记录不交付字段，撤销版本授权返回 403。`recordId` 是资源选择，不能替代授权；租户档案表单仍可省略该字段。

这份固定摘要不含申请明细、审批历史、附件或 PDF 输出。新增业务表单应由数据所有者贡献固定 Schema 与窄绑定源，不能在通用打印模块增加跨模块 SQL。
