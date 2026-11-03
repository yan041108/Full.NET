# 个人导入配置与任务输入

ImportExport 提供按当前租户、本人、静态 Schema 和工作表隔离的个人列映射。首批消费者为租户职位和企业申请示例。配置不是业务数据，也不授予业务操作权限。

创建导入任务时可省略 `columnMapping`，继续使用标准 XLSX 模板；显式提供时，其值是 JSON 字符串，包含 `headerColumns` 和 `columns`。每个当前目标列必须恰好绑定一个来源表头。来源名称首尾空白规范化、区分大小写且不能重复；不支持表达式、SQL、类型转换或缺失字段猜测。工作簿可重排列、使用别名及忽略额外列，最多 32 列；来源表头最长 128 字符。标准模板下载内容不随个人配置变化。

```json
{
  "headerColumns": ["code", "name", "displayOrder", "unitCode", "positionLevelCode"],
  "columns": [
    { "targetColumn": "code", "sourceHeader": "编号" },
    { "targetColumn": "name", "sourceHeader": "名称" },
    { "targetColumn": "displayOrder", "sourceHeader": "顺序" },
    { "targetColumn": "unitCode", "sourceHeader": "单位" },
    { "targetColumn": "positionLevelCode", "sourceHeader": "级别" }
  ]
}
```

`/api/v1/me/import-profiles/{schemaKey}/{worksheetKey}` 提供 GET 列表和 POST 创建；其 `/{profileId}` 提供 GET、PUT 和 DELETE。写入正文分别为 `{name, configuration, version}` 和删除的 `{version}`。创建版本为零，更新和删除须匹配最近读取的正整数版本。版本冲突返回 409，配置缺失或不属于本人返回 404。即使管理员也不能读取他人个人配置。每个本人、租户、来源和工作表最多 32 条，名称最多 64 字符，规范配置最多 16 KiB。列表仅返回有界摘要，详情才读取正文；写入在本模块作用域行锁下串行检查上限和版本。

端点分别使用 `import_export.import_profiles.read/create/update/delete`。每次请求还权威检查当前交互会话、静态目录读取权限及该来源注册的精确业务导入权限。业务来源检查在 ImportExport 本地事务之前执行；不跨模块写表或共用业务事务。

目标表头的有序 SHA-256 指纹只判断映射结构是否兼容，不表示整套业务规则版本。指纹不匹配或正文无效时，详情返回 `isCompatible=false` 和 `configuration=null`，本人可按当前目录修正保存或删除。若服务部署后目录已变化，请重新打开创建弹窗读取目录。未知写入结果及版本冲突必须先显式重读，不自动重放写入。

任务创建时将规范化映射冻结到私有输入，不保存个人配置指针。预校验、Worker、恢复和重试始终使用同一输入；后续编辑或删除配置不改变既有任务。损坏或过期的快照拒绝执行，不按标准模板回退。详情只展示有界列映射，不返回私有 JSON 或会话绑定。实际 Excel 行号用于错误定位与行幂等，检查点仍是有效数据行的序号。

创建任务前权威检查通用创建权限与来源权限。仅原上传者可明确排队、恢复或重试，并绑定该次有效交互会话。Worker 每批重新检查绑定；实际业务行写入前再次检查来源及本行使用的字段权限。后台不会自行寻找新会话，无绑定旧队列失败关闭；原上传者明确重试可以重绑其当前有效会话，但原映射、能力上限和已解析计划保持。两类业务都复用原模块事务和任务/行幂等回执。

配对迁移为 `357_ImportProfilesAndInputSnapshots.sql`，包含个人配置、本人作用域锁及任务的可空私有输入列。部署失败可重放，旧队列不回填授权。移除这些结构会丢失偏好或冻结输入，回退前须停用消费者并保存数据。MySQL 只有迁移连接启用固定 DDL 所需的用户变量，业务连接保持禁用。

XLSX 限制保持 1 MiB 上传、4 MiB 解压、2 MiB XML、1,000 数据行，并拒绝公式和外部关系。仅显式映射模式的列宽与共享字符串预算扩到有限的 32 列。该能力不引入动态 Schema、脚本、跨模块写事务、缓存或事件发布。

停止后续调度的状态、所有权及竞争语义见[本人待执行导入取消](import-task-cancellation.md)。取消也不会改变已经冻结的任务输入。
