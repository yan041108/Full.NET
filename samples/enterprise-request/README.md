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

正式 Worker profile 的后台宿主恢复验证与独立 Worker 操作系统进程崩溃验证需分别记录；样例生成 CRUD 的完整 Native AOT 运行仍需独立验收。
