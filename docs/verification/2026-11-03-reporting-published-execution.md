# 已发布报表执行与列键验证

基线 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，快照 `foundation-c02-report-published-execute-20261103`。修复执行页把草稿参数与服务端最新发布版混用，页面改为明确读取和执行同一发布版本；冻结分页输入并补齐上下文失效。

| 范围 | 实际结果 |
| --- | --- |
| Vue 页面及适配器 | 2 文件 16/16；独立权限、发布参数/版本固定、翻页快照、改输入后重新执行、选择及身份取消、失败手动恢复、响应身份和单元格类型 |
| 源生成序列化 | 7/7，零跳过；保留大小写不同的机器码、null/中文键、字面文本往返，拒绝 null 字典、数组、结构化/数字单元格及重复键 |
| 定向 Release 构建 | Reporting.Contracts、Reporting、薄单元及集成项目均零警告/错误 |
| Reporting AOT 分析 | 零警告/错误，复用原隔离输出；未做完整 Native 发布 |
| 真实执行页面 | 1/1，SQL Server + Kestrel + Vue + Edge；未发布草稿换成另一套参数，页面不读取草稿，仍固定执行发布版本 1，原始 EngineVersion 列及 2026.1 单元格实际可见 |
| 页面验收 | 1440/390px、执行区域 axe 通过，截图复核通过；外部连接使用受控 Port，真实查询管道执行一次，不冒充厂商连接认证 |
| 运行时 OpenAPI | 实际导出 1/1，规范化后与现行快照逐字节一致；确定性 SDK 和离线快照检查通过，无无意义重生成 |
| Vue 类型检查及生产构建 | 通过 |

首轮适配器验证发现生成的字典守卫未检查单元格值，已补字符串/null 校验；类型检查按实际验证收窄未知字典，不弱化运行时判断。真实页面又复现后端列键缺陷：列声明为 EngineVersion，字典却序列化为 engineVersion，导致成功响应的表格为空白。现在仅在 ReportingExecutionRow.Values 上使用静态属性转换器，原样写列键，未改变全站 DictionaryKeyPolicy，也没有客户端大小写兼容层。修复后页面和实际 HTTP 断言一起通过；早期失败不计通过。

实现按微软[源生成说明](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation#source-generation)及 [Utf8JsonWriter](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter) 核对，读写使用显式令牌与原始键，转换器不走反射。现有 SQL/提供程序与数据库行为未改，因此未自动扩为双库或整个 Reporting 验收；当前变更的 JSON/源生成边界和实际 API 已验证。

首屏 72 个静态块，1,831,579 / gzip 449,684 字节，仍超过旧预算及 5% 容差。执行页面 9,936 / gzip 4,026 字节，保持懒加载，未进入静态首屏。没有修改预算。容量仍为 `Capacity-not-verified`，未执行全量测试或完整 Native 发布。

证据：`C:\Users\dai27\AppData\Local\Temp\fullnet-report-execute-evidence-f0659ad380c0466f91763d4c8bce3cb3`，含单元 TRX、真实页面 JSON/截图、原始及规范化 OpenAPI、来源摘要、构建/测试/AOT 日志与包体摘要。继续复用此前清理被自动审批拒绝的 AOT/Vite 缓存，没有绕过删除限制。
