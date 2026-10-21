# 全项目性能审查与修复记录（2026-10-20）

## 状态与审查基线

- 状态：**共 80 项（13 项 P1），修复与本地受影响集中验收已完成；环境依赖跳过和容量边界见文末**。前三十五轮 PERF-01～PERF-80 保留；第三十六轮复核未新增。
- 保存依据：保留历轮审查证据；用户后续已授权批量修复 80 项、最后统一测试。实施状态见 [批量计划](../superpowers/plans/2026-10-20-performance-backlog-remediation.md)。
- 分支：`main`。
- HEAD：`49345c3c9823360071433adb4928e09691a35d62`。
- 实际输入：审查时当前工作区，含此前修复及其他未提交改动（`git status --short` 共 285 项）；HEAD 不能单独还原本轮审查输入。
- 范围与方法：只读检查后端数据库访问、后台消费者、文件恢复及清理、授权与租户调用边界，以及 Vue 管理端加载链；沿入口、配置和 SQL 核对下列触发条件。
- 审查证据边界（实施前）：以下问题发现主要来自静态调用链，有界局部诊断在对应条目单独注明；审查阶段未执行构建、回归、真实数据库复现或性能压测。后续实施与集中验证见文末记录；未量化端到端延迟、吞吐及资源影响，容量状态保持 `Capacity-not-verified`。
- 与既有批次的关系：本清单为新的待处理项，不代表[此前性能修复验证](performance-remediation-2026-10-19.md)中的问题重新打开。
- 追加轮次（2026-10-20）：仍为同一分支和 HEAD，输入为当前工作区，追加前 `git status --short` 共 286 项，包含本清单。继续核对 Kafka 宿主、OIDC 保留清理、钉钉镜像同步、知识索引后台授权、代码生成检查点清理及 OSS HTTP 读取链；只向同一清单追加独立问题，不重复登记 PERF-01～PERF-05。
- 第三轮（2026-10-20）：用户要求排除已记录的 11 项继续审查；分支、HEAD 和追加前工作区状态数量均未变化。本轮沿支付回调事务、审批恢复、GoView 项目及版本、MQTT 发布和历史查询链核对新发现，并检查导出、预览、缓存及其他后台路径。仅追加 PERF-12～PERF-16，不修改实现；未执行构建、回归或压力测试。
- 第四轮（2026-10-20）：用户要求排除已记录的 16 项继续审查；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查流水号锁、Workflow 批量操作、后台转换、OIDC 撤销、跨模块受众目录与统计、日志前端加载等路径，仅追加 PERF-17～PERF-21。流水号规则的共享读锁、报表静态查询的既有上限等未确认缺陷的路径不作为新问题；未修改实现或运行构建、回归、压测。
- 第五轮（2026-10-20）：用户要求排除已记录的 21 项继续审查；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查 AI Agent 准入与租约、Jobs 派发与执行、Kafka 范围回放，并沿 OCR/K3Cloud 外部调用、本地日志、树形查询与前端加载路径筛查；仅追加确认独立的 PERF-22～PERF-24。未修改实现，未执行构建、回归或压测。
- 第六轮（2026-10-20）：以当前清单的 24 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查备份下载、打印模板、代码生成 Git 进程、AI 流式输出、日志时间线、用户批量写入及通知实时刷新，追加 PERF-25～PERF-27。已有有界尾读、聊天输入输出上限、徽标刷新合并等边界不重复登记；仅更新本文件，未修改实现或运行构建、回归、压测。
- 第七轮（2026-10-20）：以当前清单的 27 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮沿签名认证及过期回收、外部 OAuth/OIDC 登录、打印模板、支付对账、数据库连接租约、MCP 客户端和监控读取筛查，追加 PERF-28～PERF-31，均为 P2。非事务连接按次释放、MQTT 限流状态容量、MCP 初始化复用等已有边界不作为新问题；未修改实现，未运行构建、回归或压测。
- 第八轮（2026-10-20）：以当前清单的 31 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查租户权益与配额对账、Workflow 图编译与办理人发布校验、知识检索及授权扫描、审计详情回收和前端 Agent 事件消费，追加 PERF-32～PERF-34。知识检索已有来源数、行数及正文投影限制；增量授权扫描明确约定每轮每授权最多一个新来源，本轮未将其固定间隔直接登记为缺陷。仅更新文档，未执行构建、回归、崩溃复现或压测。
- 第九轮（2026-10-20）：以当前清单的 34 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 LDAP 运维调用、Document 标签/分享/ACL、日志时间线及趋势、数据库元数据读取与租户资源文件路径，追加 PERF-35～PERF-37，均为 P2。区分 LDAP 的同步执行边界和搜索结果预算，两项可分别修复，不能互相替代；仅更新本文件，未修改实现，未执行构建、回归、真实 LDAP 或数据库复现、压测。
- 第十轮（2026-10-20）：以当前清单的 37 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 MCP 远端工具发现/批准/执行、知识库成员设置、Prompt 版本、Workflow 表单与附件以及 SMTP 传输，追加 PERF-38～PERF-40，均为 P2。另读取锁定的 MCP 2.2.0 本地 NuGet 包 XML 文档，核对所用 ListToolsAsync 重载的分页及缓存行为；表单字段数量、知识库成员数量等既有上限在相应结论中保留。仅修改本文件，未执行构建、回归、真实 MCP/数据库复现或压测。
- 第十一轮（2026-10-20）：以当前清单的 40 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查公告接收列表/详情/未读统计、通知投递列表、绑定及提供方版本投影、附件加载、收件人目录和日志导出边界，追加 PERF-41～PERF-42（P1、P2 各一项）。绑定/提供方列表已有禁止补查版本的分支，附件加载已有单件与总字节限制，不把这些已有边界登记为问题。仅修改本文件，未执行构建、回归、真实数据库复现或压测。
- 第十二轮（2026-10-20）：以当前清单的 42 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Calendar 日程、Platform 更新日志/备份下载、Workflow 实例读取、OCR/K3Cloud 外部调用及 DI 注册，并筛查 Vue 实体列表加载，追加 PERF-43～PERF-44，均为 P2。日程及更新日志已有数据库分页，备份下载返回文件流；既有的用户页全量下载等问题不重复登记。两项新增分别针对响应读取预算和传输生命周期，可独立处理，不能相互替代；仅修改本文件，未执行构建、回归、真实 Provider/数据库复现或压测。
- 第十三轮（2026-10-20）：以当前清单的 44 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮核对 Auditing/认证事件清理、Workflow 超时扫描、AI 知识清理、Office 预览领取、导入错误回执/报表工作簿生成、打印布局预处理及代码生成元数据路径，追加 PERF-45～PERF-46，均为 P2。认证事件已有分批删除、Workflow 超时已有跨 Scope 游标，报表工作表已有逐行 XML 写入，不将这些现有边界登记为问题。对打印正则执行一次有 100ms 超时保护、最大 32768 字符的局部诊断，结果记入 PERF-46；仅修改本文件，未执行项目构建、回归、真实数据库复现或负载压测。
- 第十四轮（2026-10-20）：以当前清单的 46 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Host.Api 限流与认证顺序、签名请求规范化、Webhook 事件入队、AG-UI 持久事件轮询，以及服务器监控、日志文件控制、批量缓存失效和静态 Dapper 命令注册，追加 PERF-47～PERF-49（P1 一项、P2 两项）。批量失效已有全局并发与输入预算，Dapper 命令计划按启动期稳定语句名登记，不将其误报为运行时 SQL 文本无界缓存。仅修改本文件，未执行构建、回归、真实 HTTP/数据库复现或压测。
- 第十五轮（2026-10-20）：以当前清单的 49 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Organization 职位工作簿与 ImportExport 批次执行、Settings 配置批量写入/枚举字典生成、Host/Tenant 文件批量操作，以及 Agent 页面事件处理和图表更新，追加 PERF-50～PERF-51，均为 P2。保留工作簿 1000 行、1 MiB 上传及解压/XML 限额、Host 文件批量数量等已有边界；不把存在明确上限的路径直接判定为无界。仅修改本文件，未执行构建、回归、真实数据库/存储复现或压测。
- 第十六轮（2026-10-20）：以当前清单的 51 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查事件交付状态及所有权解析、Kafka 调度/滞后采样、Jobs 执行及续租、Host 文件 Claim/下载、租户资源文件对账、退款恢复和本地 Office 转换，追加 PERF-52～PERF-53，均为 P2。租户资源文件已有共享续扫游标，Jobs 已有执行续租，Kafka 遥测状态有数量限制；Office 子进程已有时限、取消终止及输出管道排空，不将这些已实现的保护误报为缺失。仅修改本文件，未执行构建、回归、真实数据库/对象存储/转换器复现或压测。
- 第十七轮（2026-10-20）：以当前清单的 53 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Identity 签名请求体、租户成员/在线会话、机构投影与字段授权、Host 工作台、本地化，以及 Tenancy 生命周期、权益回填和 MetricId 修复路径，追加 PERF-54～PERF-55，均为 P2。签名正文已有硬上限，合法字段授权受固定目录约束，机构投影已有 keyset 页面；不将这些已有边界误述为缺失。在线会话调用的 OIDC 撤销广播与 PERF-17 有共享根因，本轮不重复登记。仅修改本文件，未执行构建、回归、真实 HTTP/数据库复现、分配基准或压测。
- 第十八轮（2026-10-20）：以当前清单的 55 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Regions 数据集预览/应用、聊天会话历史及模型上下文、报表定义与代码生成模板查询、Workflow/AI 候选目录和 Vue 加载路径，追加 PERF-56～PERF-58（P1 一项、P2 两项）。区分区域导入的重复读取和 replace 删除参数预算，两者可独立修复；聊天模型输入已有 40 条/64000 字符限制，但该限制不覆盖会话详情。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现或压测。
- 第十九轮（2026-10-20）：以当前清单的 58 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续核对 Reporting 定义/版本及执行页、CodeGeneration 模板规范化与元数据、AI 工具审计及反馈审核、Document 版本保留、审计微批和 K3Cloud 同步调用边界，追加 PERF-59（P2）。代码生成模板已有每页 100 条、每份 128 列上限，反馈审核与文档清理已有窗口/批次预算，未仅凭完整映射或循环形态将其登记为新缺陷。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现或压测。
- 第二十轮（2026-10-20）：以当前清单的 59 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查其他 Vue 管理页分页和刷新、租户邀请与权威租户解析、Calendar 查询、国密调用、外部数据库准入、日志文件预算、代码生成编译子进程以及文件/导出/预览任务，追加 PERF-60～PERF-61，均为 P2。同类管理页的独立 fetchAll 实现合并登记，未按页面数量拆成多项；文件树、区域树、外部数据库并发及导出输入已有相应预算，未将这些边界误报为缺失。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现或压测。
- 第二十一轮（2026-10-20）：以当前清单的 61 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查 Workflow 运行时办理人解析、加签/抄送、通知投递槽位、日志与服务器监控、代码生成下载/打包、缓存序列化和文件资源处理，追加 PERF-62（P2）。区分发布前角色存在性检查与运行时解析超限后的多余工作；加签已有 10 人上限，通知已有即时执行槽位领取及外发前续租，不将这些保护误报为缺失。仅修改本文件，未执行构建、回归、真实数据库复现、CPU 基准或压测。
- 第二十二轮（2026-10-20）：以当前清单的 62 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Document 匿名分享/预览轮询与口令验证、知识索引 PDF/DOCX 清单及激活校验、Embedding 输入、模型连通性、备份下载和 HTTP 日志路径，追加 PERF-63～PERF-64，均为 P2。DOCX 已有共享已验证清单的 ReadBatch 重载，不将其与 PDF 重复解码混为一谈；模型探测已有 64 KiB 响应预算，匿名分享已有专用限流，保留这些边界。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现、CPU 基准或压测。
- 第二十三轮（2026-10-20）：以当前清单的 64 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查机构更新与父链校验、授权树投影、Realtime/L2/数据库健康检查及生命周期、通知提供方和其他批次/恢复入口，追加 PERF-65～PERF-66，均为 P2。授权目录按启动期静态目录投影，L2 健康检查复用受管理缓存，不将其误报为动态无界目录或新建连接；Realtime 独立短连接的设计目的与取消后的所有权缺口分开评价。仅修改本文件，未执行构建、回归、真实数据库/Redis 复现或压测。
- 第二十四轮（2026-10-20）：以当前清单的 66 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查组织负责人解析与投影对账、当前会话授权、Prompt/Agent 查询、通知收件人及路由、Jobs 健康状态与心跳、OCR 和编译取消路径，追加 PERF-67（P2）。通知收件人已有批量目录、Prompt 已有分页；MCP 会话摘要通过既有全历史查询读取，归入 PERF-58 的共享根因，不另立重复项。Jobs 心跳服务为 Singleton，同一进程复用实例行，新增风险按进程启动历史累计而非按每次轮询累计。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现或压测。
- 第二十五轮（2026-10-20）：以当前清单的 67 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查租户订阅与商业重新启用门禁、品牌配置、更新日志、流水号分配、表格偏好缓存、检查点容量统计、退款恢复及日志时间线，追加 PERF-68（P2）。确认订阅唯一性只约束活动状态，不限制已取消历史；不将已有活动唯一索引误述为缺失，也不将流水号原子计数串行化直接判为性能缺陷。仅修改本文件，未执行构建、回归、真实数据库/浏览器复现或压测。
- 第二十六轮（2026-10-20）：以当前清单的 68 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查审计日志工作簿导出、异常摘要写入、枚举字典生成、租户权益回填、知识召回/分块与 Tokenizer、报表工作簿生成，追加 PERF-69（P2）。审计导出已有时间窗、行数和敏感字段授权；异常中间件使用固定安全消息及空堆栈，不将任意大堆栈作为新发现的前提。仅修改本文件，未执行构建、回归、真实数据库导出、分配基准或压测。

- 第二十七轮（2026-10-20）：以当前清单的 69 项为排除范围；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查通知及支付外部 HTTP 调用、AI 知识清理与增量恢复、打印和 MCP 路径，追加 PERF-70（P2）。通知及支付客户端已有 HttpClient 工厂和 30 秒超时，本项仅登记独立客户端的响应字节预算缺口；知识清理已有批量上限、增量恢复已有续扫游标，不将其误报为无界扫描或前缀饥饿。仅修改本文件，未执行构建、回归、真实提供方调用或压测。

- 第二十八轮（2026-10-20）：用户要求排除已记录的 70 项继续审查；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Identity 角色数据范围、机构本地投影及运行时范围组合、文档版本保留、文件墓碑清理、导入检查点/错误回执、工作台聚合和 Vue 聊天消费路径，追加 PERF-71（P2）。文档保留已有单轮删除预算，工作台按权限裁剪并发聚合；角色自定义范围运行时使用子查询，不将其误述为机构 ID 全量展开造成参数超限。仅修改本文件，未修改实现，未执行构建、回归、真实数据库复现或压测。

- 第二十九轮（2026-10-20）：用户本轮仍写排除 70 项，实际清单已有 71 项，按全部既有条目去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查支付/退款对账及证书缓存、缓存输入边界、代码生成目录与编译输出、Settings 域审计差异及双库迁移、服务器监控和日志尾读，追加 PERF-72～PERF-73，均为 P2。支付证书并发回源已属于 PERF-12，不重复登记；日志尾读和缓存反序列化已有预算。本轮只追加静态风险，未修改实现，未执行构建、回归、真实数据库执行计划或负载测试。

- 第三十轮（2026-10-20）：用户本轮仍写排除 70 项，按实际已有的 73 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮核对 Files 目录/元数据搜索、Document 版本及保留策略、Organization 隶属与岗位目录、Tenancy 权益读取和 AI 历史来源复核/向量重排，追加 PERF-74～PERF-75，均为 P2。目录树已有单层/深度预算，AI 来源复核及重排已有候选数量限制，不将其误报为无界处理。仅修改本文件，未修改实现，未执行构建、回归、真实数据库/浏览器复现或压测。

- 第三十一轮（2026-10-20）：用户本轮仍写排除 70 项，按实际已有的 75 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查访问会话及当前授权、AI 来源完整性/召回/分块边界、Jobs 执行查询与清空、Workflow 实例列表、站内信分页及 Outbox 保留清理，追加 PERF-76（P2）。Jobs 积压统计已有 pending 条件，Outbox 清理已有批量预算，不将其误述为扫描全部终态历史或无界删除；授权及来源完整性复核不因查询较多而直接判为可省略。仅修改本文件，未修改实现，未执行构建、回归、真实数据库删除或压测。

- 第三十二轮（2026-10-20）：用户本轮仍写排除 70 项，按实际已有的 76 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮继续检查 ImportExport 批次执行与 Organization 行回执、Reporting 导出分页/生成/上传及双库领取和完成 SQL，追加 PERF-77（P2）。导入已有行级幂等，两个模块都有 LeaseId 终态/进度守卫，不将慢任务重复工作直接等同于重复业务写入或陈旧完成覆盖。仅修改本文件，未修改实现，未执行构建、回归、双 Worker 复现、真实数据库或对象存储压测。

- 第三十三轮（2026-10-20）：用户本轮仍写排除 70 项，按实际已有的 77 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Kafka 死信及 Shadow 对比、Worker 注册/配置、通知派发、Identity 菜单与注册邀请，追加 PERF-78（P2）。Shadow 对比默认关闭且不执行正式业务 Handler，本项按明确启用后的持续观测场景登记；不将其误述为所有 Worker 的常态内存增长。仅修改本文件，未修改实现，未执行构建、回归、真实 Kafka 长时消费或内存压测。

- 第三十四轮（2026-10-20）：用户本轮仍写排除 70 项，按实际已有的 78 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 LogConsumer 分区水位与批次交付、Kafka 在途状态、AI 生成注册清理、HTTP Job 发送/错误摘要及 Runner 取消/续租，追加 PERF-79（P2）。LogConsumer 水位已有 maxPending，AI 注册由单代任务 finally 清理；HTTP Job 错误正文已有 2048 字节读取上限，本项只登记未贯通的读取时间预算。仅修改本文件，未修改实现，未执行构建、回归、慢 HTTP 复现或压测。

- 第三十五轮（2026-10-20）：按实际已有的 79 项全部去重；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查 Workflow 实例/待办/执行日志、DataApproval 请求列表、流程设计器父子同步与递归节点监听，并筛查文件下载路径，追加 PERF-80（P2）。后端执行日志已有 Take=200，不误报为全历史读取；设计器已有防循环回写措施，本项登记重复遍历及克隆成本，不声称存在无限回写。使用已安装 Vue 3.5.42 执行一次最大 80 节点的内存机制诊断，结果写入该条目；仅修改本文件，未修改实现，未执行构建、项目回归、真实浏览器复现或负载压测。

- 第三十六轮（2026-10-20）：排除已记录的 80 项；仍为同一分支和 HEAD，追加前工作区共 286 项。本轮检查数据库准入、本地日志写入与回收、Kafka 控制面/Producer、登录会话策略、用户媒体引用、SMTP、更新日志分页、配额查找、账号挑战及 AI 解析沙箱。未确认具有充分证据且独立于既有条目的新增性能问题，不为补足数量重复登记。排除依据包括：[DatabaseAdmissionGate.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DatabaseAdmissionGate.cs) 已限制排队及等待，[LocalFileLogStore.cs](../../src/BuildingBlocks/Full.NET.Hosting/Observability/LocalFileLogStore.cs) 已限制自有文件数、磁盘占用及运行期清理量；[双库媒体迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/128_IdentityUserProfileAvatarSignature.sql)及[MySQL 对应迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/128_IdentityUserProfileAvatarSignature.sql)已有头像/签名文件索引；[TenantQuotaReservationService.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReserveTenantQuota/TenantQuotaReservationService.cs) 第 325–330 行仅在 default/当前月份两种计量期间查找；[NotificationDeliveryBatchProcessor.cs](../../src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryBatchProcessor.cs) 第 191–193 行给后台 Provider（含 SMTP）传入租约 0.8 倍的取消期限；PDF/DOCX 解析沙箱已有输入输出字节、执行及清理预算。登录策略复用的会话逐条广播与 PERF-17 风险重叠，不另立编号；不能仅凭每次新建 SMTP 客户端、循环或历史表未见清理，就断言它们已构成独立性能瓶颈。仅补充本轮复核记录，全部 PERF 条目正文不变，未修改实现，未执行构建、回归、真实数据库/网络复现或压测。

## 问题总览

| 编号 | 优先级 | 问题 | 状态 |
| --- | --- | --- | --- |
| PERF-01 | P1 | Host 文件后台游标在每轮重置，失败前缀可能永久阻塞后续记录 | 已修复，集中验证完成 |
| PERF-02 | P1 | 机构参考接口全量展开用户 ID，可能超过 SQL Server 参数上限 | 已修复，集中验证完成 |
| PERF-03 | P2 | 用户管理页串行下载全部用户，再在浏览器分页 | 已修复，集中验证完成 |
| PERF-04 | P2 | Webhook 整批预占租约后串行发送，后部记录可能在执行前过期 | 已修复，集中验证完成 |
| PERF-05 | P2 | 通知模板列表按模板键串行补查语言状态，产生 N+1 查询 | 已修复，集中验证完成 |
| PERF-06 | P1 | Kafka 首个消费组同步 Poll 阻止后续消费组启动 | 已修复，集中验证完成 |
| PERF-07 | P1 | 钉钉镜像状态不变时不推进扫描排序时间，旧流程饿死后续候选 | 已修复，集中验证完成 |
| PERF-08 | P2 | OIDC 保留清理单条 DELETE 无批次上限 | 已修复，集中验证完成 |
| PERF-09 | P2 | 知识索引同一授权检查点逐权限重读会话与完整权限快照 | 已修复，集中验证完成 |
| PERF-10 | P2 | 检查点清理逐候选读取全部受管文件正文，却只使用 Manifest | 已修复，集中验证完成 |
| PERF-11 | P2 | OSS 错误响应正文无显式字节和读取时限，可能阻塞文件操作 | 已修复，集中验证完成 |
| PERF-12 | P1 | 微信支付回调在数据库事务内等待平台证书 HTTP 回源 | 已修复，集中验证完成 |
| PERF-13 | P1 | DataApproval 恢复扫描被停用租户前缀阻塞 | 已修复，集中验证完成 |
| PERF-14 | P2 | GoView 列表无界返回全部画布正文，历史版本同样全量读取 | 已修复，集中验证完成 |
| PERF-15 | P2 | MQTT 每条发布都建立和断开 Broker 连接 | 已修复，集中验证完成 |
| PERF-16 | P2 | MQTT 主题包含搜索没有强制时间范围，查询成本随历史增长 | 已修复，集中验证完成 |
| PERF-17 | P1 | OIDC 授权撤销事务等待逐会话实时广播及重试 | 已修复，集中验证完成 |
| PERF-18 | P2 | Office HTTP 转换成功响应无输出字节上限，完整缓冲后才进入上传校验 | 已修复，集中验证完成 |
| PERF-19 | P2 | 公告受众逐条校验、逐条写入，无目标数量上限且占用长事务 | 已修复，集中验证完成 |
| PERF-20 | P2 | 访问日志页自动拉完全部历史游标，默认每次仅取 20 条 | 已修复，集中验证完成 |
| PERF-21 | P2 | 公告机构受众计数逐机构读取全部成员，在应用内去重 | 已修复，集中验证完成 |
| PERF-22 | P1 | AI Worker 心跳等待整个运行批次完成，长任务导致新 Run 准入误拒绝 | 已修复，集中验证完成 |
| PERF-23 | P2 | Jobs 到期计划派发等待整批任务执行，调度延迟随任务耗时增长 | 已修复，集中验证完成 |
| PERF-24 | P2 | Kafka 回放不暂停已完成分区，无关实时消息持续消耗读取预算 | 已修复，集中验证完成 |
| PERF-25 | P1 | 代码生成 Git 发布无执行期限且忽略请求取消，长期占用工作区独占锁 | 已修复，集中验证完成 |
| PERF-26 | P2 | Host 用户 JSON 导入缺少行数上限，逐行密码散列及事务可超出工作簿预算 | 已修复，集中验证完成 |
| PERF-27 | P2 | 站内信实时提示直接触发页面重查，缺少合并与并发上限 | 已修复，集中验证完成 |
| PERF-28 | P2 | 签名认证 Nonce 只写入不回收，过期记录持续累积 | 已修复，集中验证完成 |
| PERF-29 | P2 | OAuth 发起请求同步删除全部过期状态，将清理积压带入登录链 | 已修复，集中验证完成 |
| PERF-30 | P2 | 外部 OIDC 登录重复加载发现文档，每次验令牌新建配置管理器 | 已修复，集中验证完成 |
| PERF-31 | P2 | 打印模板及版本列表全量返回 HTML 正文，选择页承担无关传输 | 已修复，集中验证完成 |
| PERF-32 | P1 | 工作流图无节点或路径深度预算，递归编译可能耗尽调用栈 | 已修复，集中验证完成 |
| PERF-33 | P2 | 工作流发布逐来源读取全部角色成员，只用于判断是否存在成员 | 已修复，集中验证完成 |
| PERF-34 | P2 | 配额基线对账全量扫描后逐租户串行聚合，缺少分批与续扫边界 | 已修复，集中验证完成 |
| PERF-35 | P2 | LDAP 运维方法以 Task 返回同步 Bind/Search，慢目录占用请求线程 | 已修复，集中验证完成 |
| PERF-36 | P2 | LDAP 预览只在完整响应后截断，MaxEntries 未传给搜索请求 | 已修复，集中验证完成 |
| PERF-37 | P2 | 文档 ACL 整体重写无数量上限，同一事务内逐条插入 | 已修复，集中验证完成 |
| PERF-38 | P2 | MCP 工具批准在数据库事务内等待远端初始化与目录读取 | 已修复，集中验证完成 |
| PERF-39 | P2 | MCP 工具目录每次全分页回源，执行单个工具也读取全部目录 | 已修复，集中验证完成 |
| PERF-40 | P2 | 知识库成员设置在 Tenant 分支逐人查询活动成员，形成 N+1 | 已修复，集中验证完成 |
| PERF-41 | P1 | 公告接收分页及未读统计全量读取正文，机构候选 ID 可能超过参数上限 | 已修复，集中验证完成 |
| PERF-42 | P2 | 通知投递列表逐行补查全部重试记录，100 行页面执行 102 次查询 | 已修复，集中验证完成 |
| PERF-43 | P2 | OCR/K3Cloud 响应完整缓冲后才解析或截断，缺少应用级读取字节预算 | 已修复，集中验证完成 |
| PERF-44 | P2 | OCR/K3Cloud 每次操作新建并释放 HTTP Handler，连接池无法跨操作复用 | 已修复，集中验证完成 |
| PERF-45 | P2 | 审计清理低批次预算每轮固定从 Access 开始，后续类别永久饥饿 | 已修复，集中验证完成 |
| PERF-46 | P2 | 打印布局脚本剥离正则无超时，未闭合标签导致重复扫描及事务驻留 | 已修复，集中验证完成 |
| PERF-47 | P1 | 全局限流先于认证执行，已认证用户实际共享来源 IP 额度 | 已修复，集中验证完成 |
| PERF-48 | P2 | Webhook 单事件一次加载全部订阅并逐条写入投递，缺少扇出预算 | 已修复，集中验证完成 |
| PERF-49 | P2 | AG-UI 无新事件时仍逐轮读取预算和全部步骤摘要，每轮五次 SQL | 已修复，集中验证完成 |
| PERF-50 | P2 | 职位导入每批重新读取、解压和预校验整份工作簿，工作随批次数重复 | 已修复，集中验证完成 |
| PERF-51 | P2 | 系统配置批量写入无条目上限，逐项 SQL 形成长事务并存在集合参数风险 | 已修复，集中验证完成 |
| PERF-52 | P2 | 事件交付状态按每条事件流串行读取所有权，产生 N+1 查询 | 已修复，集中验证完成 |
| PERF-53 | P2 | 本地 Office 转换只限制时间，临时产物没有生成期间的磁盘字节预算 | 已修复，集中验证完成 |
| PERF-54 | P2 | 签名验签为正文散列额外创建两份完整正文缓冲，放大分配与复制 | 已修复，集中验证完成 |
| PERF-55 | P2 | 配额 MetricId 修复全量读取待修复预留，再逐行查询同一指标 | 已修复，集中验证完成 |
| PERF-56 | P2 | 行政区域导入快照按全表逐条补查，应用阶段重复读取同一快照 | 已修复，集中验证完成 |
| PERF-57 | P1 | 行政区域 replace 一次展开全部删除编码，可能超过 SQL Server 参数上限 | 已修复，集中验证完成 |
| PERF-58 | P2 | 聊天详情读取全部消息，发送完成及生成状态刷新反复加载完整历史 | 已修复，集中验证完成 |
| PERF-59 | P2 | 报表定义及版本列表无分页并读取完整布局，执行页也先全量加载再筛选 | 已修复，集中验证完成 |
| PERF-60 | P2 | API Key、配置、字典等其他管理页串行拉完所有分页，刷新重复全量加载 | 已修复，集中验证完成 |
| PERF-61 | P2 | 我的租户邀请全量读取后逐条权威解析租户，同一租户重复查询 | 已修复，集中验证完成 |
| PERF-62 | P2 | 工作流办理人先全量物化并平方级去重，最后才检查 20 人上限 | 已修复，集中验证完成 |
| PERF-63 | P2 | PDF 索引逐批重复解码完整清单并重算全计划摘要，激活事务也承担该成本 | 已修复，集中验证完成 |
| PERF-64 | P2 | 匿名 Office 预览轮询超过共享限流预算，慢转换可能被自身请求中断 | 已修复，集中验证完成 |
| PERF-65 | P2 | 非根机构普通更新也读取租户全部父关系，在事务内重建整张关系映射 | 已修复，集中验证完成 |
| PERF-66 | P2 | Realtime 探针取消连接等待后，迟到成功的 Redis 连接没有释放所有者 | 已修复，集中验证完成 |
| PERF-67 | P2 | Jobs Worker 心跳实例按启动次数累积且不回收，健康接口全量返回历史实例 | 已修复，集中验证完成 |
| PERF-68 | P2 | 租户订阅创建、取消和商业门禁先读取全部订阅历史，历史弹窗也无分页 | 已修复，集中验证完成 |
| PERF-69 | P2 | 审计导出构建完整工作表 DOM，SQL 完成后不再响应请求取消 | 已修复，集中验证完成 |
| PERF-70 | P2 | 通知及支付客户端完整缓冲外部响应，缺少业务级响应字节预算 | 已修复，集中验证完成 |
| PERF-71 | P2 | 角色自定义数据范围无机构数量预算，逐机构校验并在事务内逐条插入 | 已修复，集中验证完成 |
| PERF-72 | P2 | Settings 域审计差异仅按 TraceId 查询，双库缺少匹配的前导索引及时间范围 | 已修复，集中验证完成 |
| PERF-73 | P2 | 代码生成读取单个对象元数据前，全量读取并排序表/视图目录 | 已修复，集中验证完成 |
| PERF-74 | P2 | 文档版本历史无分页并全量渲染，回退与删除后再次完整刷新 | 已修复，集中验证完成 |
| PERF-75 | P2 | Host 文件名包含搜索缺少候选范围预算，每次分页先统计完整匹配集 | 已修复，集中验证完成 |
| PERF-76 | P2 | Jobs 清空某作业全部终态历史使用单次无批量上限 DELETE，形成随历史增长的事务 | 已修复，集中验证完成 |
| PERF-77 | P2 | 导入与报表导出执行期间不续租或按租约停止，慢任务过期后可被并发重领并重复工作 | 已修复，集中验证完成 |
| PERF-78 | P2 | Shadow 对比 Worker 的 EventId 去重集合随进程内累计观测事件数增长，没有容量或观测窗口 | 已修复，集中验证完成 |
| PERF-79 | P2 | HTTP Job 错误摘要读取未使用请求超时令牌，错误正文首字节停顿可持续占用执行槽位 | 已修复，集中验证完成 |
| PERF-80 | P2 | 流程设计器递归节点逐层深度监听完整子树，编辑时叠加重复遍历及整树克隆 | 已修复，集中验证完成 |

## PERF-01：Host 文件后台扫描饥饿

**优先级：P1。**

代码位置：

- [PendingHostFileReconciliationRunner.cs](../../src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReconciliationRunner.cs)，第 52–54 行初始化本轮游标。
- 同类路径：[DeletedHostFileBlobCleanupRunner.cs](../../src/Modules/Full.NET.Modules.Files/Cleanup/DeletedHostFileBlobCleanupRunner.cs)、[PendingHostFileReferenceClaimReconciliationRunner.cs](../../src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationRunner.cs)。

每次 `RunOnceAsync` 都从头初始化游标；游标只在本轮的批次之间推进。上传对账默认 `BatchSize=100`、`MaxBatchesPerRun=10`。当前缀有 1000 条持续探测失败、缺失对象但必须保留的 `publishing` 记录等不可移除候选时，每轮预算都会被同一前缀耗尽，后续本可恢复的文件长期得不到处理。Blob 清理失败及 Claim 探测失败也有相同的跨轮次进度丢失风险。

后续处理方向：保存跨轮次扫描进度，扫描到末尾后有界回绕；失败项保留并在后续轮次重试，不能通过误删墓碑、释放未知 Claim 或把未知对象状态当作不存在来腾出预算。成对保持 SQL Server/MySQL 的排序和游标比较语义。

待验证场景：

- 失败或保留前缀达到整轮预算，连续跨 DI Scope 运行后仍能处理尾部正常记录。
- 扫描到尾部后回绕，前缀故障恢复后可以再次处理。
- 分别覆盖上传对账、Blob 清理、Claim 对账，以及取消、并发完成和原保留约束。

## PERF-02：机构参考查询规模与参数上限

**优先级：P1。**

代码位置：

- [HostUserManagementReferenceService.cs](../../src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/HostUserManagementReferenceService.cs)，第 143–145 行将全部隶属用户交给目录查询。
- [HostUserDirectory.cs](../../src/Modules/Full.NET.Modules.Identity/HostUsers/HostUserDirectory.cs)，`FindHostUsersAsync`。
- [IdentitySql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs)，`ListHostUsersByIds` 使用 `IN @UserIds`。
- [DapperAotEnumerableParameterExpander.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotEnumerableParameterExpander.cs)，AOT 路径同样逐项展开集合参数。

参考接口读取全租户的有效用户机构隶属，再一次查询全部不同用户。超过 2100 个用户 ID 时，展开后的参数数量超过 SQL Server 上限，整个参考接口会失败；在达到该上限之前，全量隶属及显示信息的读取、映射和传输也随租户规模增长。职位参考还会在一次 HTTP 请求内循环拉取所有分页。

后续处理方向：按实际页面需要提供有界、可筛选的参考读取，用户显示目录使用有界批次或合适的集合参数。避免仅修复参数数量、却继续无界读取全租户。跨模块读取继续通过所属模块 Contract Port，保留可信租户与精确权限裁剪。

待验证场景：

- SQL Server/MySQL 均覆盖超过 2100 个不同隶属用户，检查参数数量和返回边界。
- 用户机构与职位均按实际查询范围加载，检查页边界、无效隶属及空结果。
- 覆盖 Host 操作者、不同租户及仅有一侧组织权限的情形，确保无跨租户或越权结果。

## PERF-03：用户管理页全量下载

**优先级：P2。**

代码位置：[UsersView.vue](../../ui/admin/src/views/UsersView.vue)，第 1067–1078 行 `fetchAllUsers`；`filteredUsers`、`pagedUsers` 在内存筛选和分页。

页面虽然呈现分页，但加载时先按每页 100 条串行下载全部用户，再交给浏览器筛选、分页。1 万用户需要先完成 100 次列表请求，每次重新承担认证、计数及字段投影；全部完成前列表加载不能结束，浏览器还需保存完整用户集合。

后续处理方向：将筛选和分页下推为服务端有界查询，只加载当前页；机构筛选通过模块端口实现，保持字段投影与敏感字段授权。与 PERF-02 的参考数据读取协同设计，避免优化一侧后另一侧仍全量加载。

待验证场景：

- 大用户量时打开页面的列表请求数与总用户数解耦，翻页只加载目标页。
- 用户名、展示名、状态、机构及受授权控制的字段筛选保持正确，返回真实服务端总数。
- 覆盖快速筛选、翻页、刷新、上下文切换及旧请求取消，防止旧结果覆盖新页面。

## PERF-04：Webhook 租约提前占用

**优先级：P2。**

代码位置：[WebhookDeliveryBatchProcessor.cs](../../src/Modules/Full.NET.Modules.Webhooks/Features/DeliverWebhooks/WebhookDeliveryBatchProcessor.cs)，第 33–42 行整批领取后串行发送；`DeliverOneAsync` 在发送前尝试续租。

默认一次领取 10 条、租约 60 秒，而每条发送可耗时约 30 秒。前部慢请求使后部记录开始处理前已超过原租约，`RenewLease` 会拒绝执行；这些记录在等待时又被提前预占，其他 Worker 无法及时领取，形成无效领取、等待和恢复开销。现有版本检查能隔离旧领取者，但不能消除这种预占。

后续处理方向：在实际执行槽可用时才领取相应记录，串行执行时避免预领整批；如采用有界并发，每项独立 Scope 和数据库会话。保留现有外发超时、SSRF 防护、签名、版本隔离、重试与取消语义。

待验证场景：

- 一批累计处理时间超过租约、单条处理仍在自身预算内时，后部记录不因排队而失去租约。
- 多 Worker 与慢目标并存时，空闲实例能领取可执行任务，旧版本不能覆盖新领取结果。
- 覆盖超时、失败重试、停机取消及至少一次投递边界，不宣称外部副作用 Exactly-Once。

## PERF-05：通知模板语言状态 N+1

**优先级：P2。**

代码位置：[NotificationTemplateService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageTemplates/NotificationTemplateService.cs)，第 390–398 行 `LoadLocaleStatesByKeyAsync`。

列表先查询总数和当前页，再对每个不同模板键串行查询语言状态。一页含 100 个不同模板键时，仅这些业务查询就达到 102 次数据库往返，延迟及连接准入开销随页内键数量增长。

后续处理方向：按当前页模板键一次批量读取语言状态并在内存分组，或通过多个结果集合并读取；SQL 保留 `TenantScopeKey` 约束，不通过跨请求缓存陈旧状态绕过一致性要求。

待验证场景：

- 分别覆盖 0、1、100 个不同模板键及同键多语言，查询次数不再逐键增长。
- 已发布语言、缺失语言及最新版本投影与原行为一致。
- SQL Server/MySQL 均覆盖相同模板键存在于不同作用域时的隔离。

## PERF-06：Kafka 后续消费组无法启动

**优先级：P1。追加轮次新增。**

代码位置：

- [KafkaConsumerWorker.cs](../../src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerWorker.cs)，第 49–52 行逐组调用 `RunConsumerGroupAsync`，第 141–178 行为同步 Poll 循环。
- [KafkaSubscriptionTests.cs](../../tests/Full.NET.IntegrationTests/Messaging/KafkaSubscriptionTests.cs)，`Two_consumer_groups_both_receive_the_same_event` 直接创建原生 Consumer，未通过该 HostedService 启动消费组。

触发条件是启用 Kafka 且订阅目录产生两个或更多不同的 `ConsumerName`。`Select(...).ToArray()` 会同步调用第一个 `RunConsumerGroupAsync`；该方法虽然声明为 `async`，但在正常运行的 Poll 循环内没有会交还控制权的 `await`，同步 `consumer.Consume(...)` 会持续执行。首次等待发生在停止后的 `finally` 排空阶段，因而第一个调用在正常服务期间不能返回 Task，枚举无法继续启动第二个组。第一组即使没有消息也会持续 Poll，其他组仍不能启动，造成其积压无法消费。传入 scheduler 的异步处理回调不等于 Poll 方法本身发生异步挂起。

后续处理方向：按项目同步 SDK 边界规则，让每个消费组拥有独立、有界且受宿主管理的执行边界，同时持有并等待全部生命周期任务。不能仅用 `ConfigureAwait(false)`，也不能以机械 `Task.Run` 或无人观察任务绕过同步 SDK 规则；保留 Poll 线程上的完成处理、重平衡、连续 Offset 提交和关闭排空语义。

待验证场景：

- 真实 HostedService 同时注册至少两个 ConsumerName，第一组空闲时第二组仍能启动并处理消息。
- 两组均有流量、其中一组 Handler 变慢时，另一组继续处理。
- 停机和任一组故障时，所有消费组及 native handle 正确退出，覆盖 drain 超时、重平衡和至少一次投递；不能以原生 Consumer 扇出测试替代宿主测试。

## PERF-07：钉钉镜像轮询饥饿

**优先级：P1。追加轮次新增。**

代码位置：

- [DingTalkApprovalSyncService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageDingTalkApprovalSync/DingTalkApprovalSyncService.cs)，第 116 行固定候选数为 20，第 329–332 行在外部状态不变时直接返回。
- [DingTalkApprovalSyncSql.cs](../../src/Modules/Full.NET.Modules.Notifications/Persistence/DingTalkApprovalSyncSql.cs)，`ListPollCandidates` 与 `ListPollCandidatesMySql` 按旧更新时间选取候选。
- [DingTalkApprovalSyncHostedProcessor.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageDingTalkApprovalSync/DingTalkApprovalSyncHostedProcessor.cs)，每轮调用后等待配置间隔，默认 60 秒。

启用镜像同步后，20 个较老且持续返回 `RUNNING` 的流程会一直处于候选前列；状态相同时没有更新 `LastSyncedAtUtc`、`UpdatedAtUtc`，也没有跨轮次游标或下一轮询时间推进。MySQL 按 `COALESCE(UpdatedAtUtc, CreatedAtUtc)` 排序时，这些旧流程可以持续阻塞后续待出站流程；SQL Server 的 NULL 排序允许新建待出站记录先进入，但其转为 running 后仍可能被旧 running 前缀阻止继续轮询。外部回调缺失或延迟时，后续流程依赖轮询恢复却无法得到机会，并反复消耗前缀查询及外部请求。

后续处理方向：将“本次成功观察”与“业务状态变化”区分，状态不变也推进轮询调度；提供稳定、有界且公平的候选选择和失败退避。不能为了腾出候选而伪造终态，不能由外部镜像驱动 Full.NET Workflow 决策，保持作用域及出站幂等边界。

待验证场景：

- 双库分别覆盖 20 个状态不变的旧 running 加第 21 个候选，连续轮询后尾部可以出站和更新状态。
- 覆盖 SQL Server NULL 更新时间与 MySQL COALESCE 排序差异，同时间戳有稳定顺序。
- 覆盖状态不变、失败、回调缺失、并发回调及取消，不以大量重复创建外部流程换取排空速度。

## PERF-08：OIDC 清理无批次上限

**优先级：P2。追加轮次新增。**

代码位置：

- [IdentityOidcRetentionRunner.cs](../../src/Modules/Full.NET.Modules.Identity/Retention/IdentityOidcRetentionRunner.cs)，`RunOnceAsync` 顺序调用两条清理命令。
- [IdentityOidcSql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/IdentityOidcSql.cs)，第 373 行起 `PruneAuthorizations`、第 635 行起 `PruneTokens`，均无 TOP/LIMIT 或候选批次。
- [IdentityOidcRetentionOptions.cs](../../src/Modules/Full.NET.Modules.Identity/Retention/IdentityOidcRetentionOptions.cs)，只有保留天数及轮询间隔，没有单批和单轮预算。

OIDC 和保留清理都显式启用后，每轮用一条 DELETE 删除全部符合条件的 token，再用另一条 DELETE 删除全部符合条件的孤儿 authorization。首次启用、缩短保留期或停机后积压时，单条命令的工作量随历史记录增长，没有应用层上限，存在长时间持锁、事务日志及 IO 峰值和超时回滚风险，可能干扰同表认证流量。这是静态的无界操作风险，尚无真实数据库压力数据；功能默认关闭不能代替启用后的资源约束。

后续处理方向：提供单批大小和单轮预算，双库分别用短事务、有界候选和安全删除实现。仍先清理符合条件的 token，再处理无任何关联 token 的失效授权；不能删除有效令牌或破坏授权引用关系。

待验证场景：

- 双库大批历史 token/authorization 的首次及积压清理，单批、单轮均不越界，后续轮次可继续排空。
- 有效 token、未过保留期记录和仍有 token 引用的授权保持不变。
- 取消、失败、并发签发及多 Worker 下检查锁等待、恢复和实际执行计划，记录实测范围。

## PERF-09：知识索引后台授权重复往返

**优先级：P2。追加轮次新增。**

代码位置：

- [AiKnowledgeIndexEnqueueService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeIndexing/AiKnowledgeIndexEnqueueService.cs)，第 18–19 行要求 5 个权限，第 29、36、43 行为 3 个检查点，第 62–65 行逐权限调用后台授权。
- [BackgroundSessionAuthorization.cs](../../src/Modules/Full.NET.Modules.Identity/Security/BackgroundSessionAuthorization.cs)，每个权限调用都会验证会话绑定并读取完整权限快照。
- [BackgroundSessionBindingValidator.cs](../../src/Modules/Full.NET.Modules.Identity/Security/BackgroundSessionBindingValidator.cs) 和 [ActiveTenantDirectory.cs](../../src/Modules/Full.NET.Modules.Tenancy/Directories/ActiveTenantDirectory.cs)，租户绑定还会权威读取租户活动状态。
- 关联调用方：[AiKnowledgeIndexRetryService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeIndexing/AiKnowledgeIndexRetryService.cs)、[AiKnowledgeIndexGrantService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/IncrementalKnowledgeIndexing/AiKnowledgeIndexGrantService.cs)、[AiKnowledgeIndexVectorStore.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexVectorStore.cs)、[AiKnowledgeIndexTextManifestStore.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexTextManifestStore.cs)。

以成功的 Enqueue 路径为例，3 个检查点各自循环校验 5 个权限，共执行 15 次后台授权。按当前绑定验证和快照实现，仅这些调用就会产生 Host 30 次、租户 45 次权威数据库查询，尚未包含 AI 本地来源、任务持久化及其他验证。向量批次与清单持久化中也重复使用此逐权限模式，成本会随索引批次数放大。此前当前 HTTP 会话的集合授权端口不覆盖这里的冻结后台会话绑定端口。

后续处理方向：在每个必要检查点内，以冻结绑定一次读取权威会话、租户及完整权限快照，逐项判定全部权限；检查点之间继续重新读取。扩展后台 Contract Port 时保留完整绑定、租户活动检查、失败关闭和超级管理员也必须通过会话/租户验证的约束，禁止跨检查点缓存或合并掉模型派发及正文交付前后的撤销检查。

待验证场景：

- 检查点内数据库查询数不再随所需权限数量线性增长，Host/租户、refresh/OIDC application 绑定均覆盖。
- 保留原检查点数量和位置，在各 IO 等待期间撤销会话、权限、租户或切换上下文，后续收费派发和交付仍拒绝。
- 任一必需权限缺失、权威源故障及取消均失败关闭；向量及 Manifest 恢复保持租约与计费语义。

## PERF-10：检查点清理读取未使用的文件正文

**优先级：P2。追加轮次新增。**

代码位置：

- [CodeGenerationCheckpointRetentionRunner.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Retention/CodeGenerationCheckpointRetentionRunner.cs)，第 176–182 行每个候选调用 `CaptureAsync`，后续仅使用 `snapshot.PreviousManifest`。
- [GenerationWorkspaceStore.cs](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWorkspaceStore.cs)，第 42 行起 `CaptureAsync` 会把 Manifest 中全部产物路径加入捕获集合，第 131 行起 `CaptureExistingFilesAsync` 逐文件读取并保存正文。
- [CodeGenerationCheckpointRetentionOptions.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Configuration/CodeGenerationCheckpointRetentionOptions.cs)，默认每轮删除预算为 20，上限 500；清理及 Apply 默认需显式启用。

清理器只是把当前 Manifest 与检查点的 PreviousManifest 比较，却为每个候选读取并解码整个 Manifest 管理的文件集合，返回的 `ExistingFiles` 正文字典没有被使用。若有 M 个受管文件、N 个候选，会产生约 N×M 次正文读取及相应字符串分配；候选被跳过时也会支付该成本。它不是复制整仓，但工作区受管文件较多或文件较大时，后台会重复制造可避免的磁盘 IO 和分配。

后续处理方向：使用符合清理目的的最小权威读取边界，保留 recovery 残骸检查、路径验证、并发变化复核和安全删除约束。不要仅为省 IO 而跨所有候选无条件复用过期状态；若决定真正校验正文，也应明确消费验证结果及资源预算，避免读取后丢弃。

待验证场景：

- 多候选、大 Manifest 下计量文件读取次数、字节和分配，证明不再逐候选物化未使用的正文。
- Manifest 不匹配、并发变化、恢复残骸、取消及损坏检查点仍按既有规则保留或拒绝。
- 正常清理、按数量溢出清理及删除预算保持正确，不误删待恢复证据。

## PERF-11：OSS 错误正文读取没有显式资源预算

**优先级：P2。追加轮次新增。**

代码位置：[OssHostFileBlobStorage.cs](../../src/Modules/Full.NET.Modules.Files/Storage/OssHostFileBlobStorage.cs)，第 237–246 行以 `ResponseHeadersRead` 返回响应，第 250–256 行 `ThrowOssFailureAsync` 对错误正文执行 `ReadAsStringAsync`。

OSS 返回非成功状态时，错误处理会完整缓冲正文，没有显式小字节上限；`RequestTimeout` 只设置在 HttpClient，而响应以 headers-read 模式交付，后续正文读取没有单独建立该超时预算。若提供程序或代理快速返回错误头，随后发送很大或持续缓慢的错误正文，调用仍会持续占用连接、内存及等待中的请求/Worker。后台调用若只使用宿主停止令牌，可能一直等到宿主取消。该路径会影响上传、删除及恢复扫描，不同于 PERF-01 的游标饥饿问题。

API 语义依据：[Microsoft .NET 10 HttpCompletionOption 文档](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)说明，`ResponseHeadersRead` 下 HttpClient 超时仅覆盖收到响应头之前的调用，正文读取须单独设定超时；`MaxResponseContentBufferSize` 也不会在此模式自动约束正文。

后续处理方向：错误摘要读取设置明确的字节与时间预算，并确保错误、取消和读取失败时响应资源均被释放；异常消息不拼接完整外部正文。成功文件读取仍保持流式传输和明确的资源所有权，不能为修复错误处理而全量缓冲成功响应。

待验证场景：

- 本地受控 HTTP 替身返回超大、分块无长度、慢速及中途断开的错误正文，读取字节与持续时间受约束。
- 多次失败后连接和流正确释放，Worker 可以继续处理其他项，正常成功下载仍为流式。
- 覆盖调用方取消、错误摘要截断及诊断脱敏，不能把原始错误正文完整写入异常或日志。

## PERF-12：支付回调在事务内等待证书回源

**优先级：P1。第三轮新增。**

代码位置：

- [PaymentWeChatNotifyService.cs](../../src/Modules/Full.NET.Modules.Payments/Features/ReceiveWeChatNotify/PaymentWeChatNotifyService.cs)，第 56–58 行先开启事务，第 102 行起在事务委托中解析平台证书。
- [WeChatPayPlatformCertificateResolver.cs](../../src/Modules/Full.NET.Modules.Payments/Connectivity/WeChatPayPlatformCertificateResolver.cs)，第 28–36 行缓存未命中后等待 `FetchPlatformPublicKeyPemAsync`。
- [WeChatNativePayClient.cs](../../src/Modules/Full.NET.Modules.Payments/Connectivity/WeChatNativePayClient.cs)，`FetchPlatformPublicKeyPemAsync` 调用 `/v3/certificates`；[PaymentsModule.cs](../../src/Modules/Full.NET.Modules.Payments/PaymentsModule.cs)将 HTTP 超时配置为 30 秒。
- [DapperCommandTransaction.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs)和 [DbSession.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DbSession.cs)：事务开始时即取得数据库连接及准入许可，直至提交或回滚才释放。

冷启动、证书缓存到期或新证书序列号的回调会在已经持有数据库事务连接的情况下等待外部网络。Resolver 的锁只保护字典读写，不合并同键在途回源，冷缓存并发回调可分别持有连接并同时拉取证书。提供程序变慢时，即使尚未更新订单，也会让数据库连接和普通准入许可随 HTTP 等待滞留，可能挤压其他请求；当前 30 秒超时并不能消除这段跨资源占用。缓存命中路径没有该 HTTP 等待，本项不把所有回调都视为回源。

后续处理方向：将外部证书读取与必要的验签前置工作放到数据库事务之外，进入本地短事务后重新确认商户配置、订单归属及所有资金状态约束，原子提交回执和订单。并发回源合并可作为辅助措施，但不能用缓存命中率替代事务边界修复；也不能因移出验签而复用已失效的商户、租户或密钥绑定。

待验证场景：

- 冷缓存与证书轮换时，并发回调等待慢 HTTP，断言外部调用期间没有持有本地数据库事务或其准入许可。
- 双库覆盖重复通知、并发同一订单、外部失败和取消，回执与订单仍原子、幂等且保持资金状态转换。
- 在外部等待期间停用商户、变更配置或订单状态，最终事务重新验证，不能应用过期校验结论。
- 同环境对比回调并发下的连接持有时间、准入等待、其他请求尾延迟与外部回源次数，不能只测热缓存路径。

## PERF-13：DataApproval 恢复的停用租户前缀饥饿

**优先级：P1。第三轮新增。**

代码位置：

- [DataApprovalRequestRecoveryBatchProcessor.cs](../../src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestRecoveryBatchProcessor.cs)，第 48–53 行在租户范围不可用时直接跳过。
- [DataApprovalRequestApplicationRecoveryBatchProcessor.cs](../../src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestApplicationRecoveryBatchProcessor.cs)，第 48–53 行相同跳过逻辑。
- [DataApprovalSql.cs](../../src/Modules/Full.NET.Modules.DataApproval/Persistence/DataApprovalSql.cs)，第 164–214 行的关联恢复及应用恢复候选 SQL，按 `SubmittedAtUtc, Id` 从头取批次。
- [DataApprovalRequestRecoveryWorkerOptions.cs](../../src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestRecoveryWorkerOptions.cs)和 [DataApprovalRequestApplicationRecoveryWorkerOptions.cs](../../src/Modules/Full.NET.Modules.DataApproval/Execution/DataApprovalRequestApplicationRecoveryWorkerOptions.cs)：默认批大小 10、轮询 2000 毫秒。

若最早 10 个候选属于停用或无法解析的租户，每轮都命中它们；跳过分支既不推进跨轮次游标，也不记录本次扫描或重试调度时间，因此 `LastRecoveryAttemptAtUtc` / `LastApplicationAttemptAtUtc` 的重试过滤无法使这些候选退让。两个恢复 Worker 会每隔约 2 秒重复租户解析和告警，后面的活动租户或 Host 请求无法进入批次。原有业务失败路径记录重试时间，并未覆盖执行前的范围不可用分支。这是 DataApproval 独立恢复链的问题，不与 PERF-01 的文件游标或 PERF-07 的外部镜像排序混为同一修复。

后续处理方向：让候选遍历进度和实际业务执行资格分离，使用有界公平扫描及适当退避，使不可执行前缀不占满后续轮次。租户重新启用后仍可重新发现原请求；保留每次实际执行前的活动租户权威解析，不能以停用租户也执行或直接清除请求来排空。

待验证场景：

- 双库分别为两种恢复链构造满批停用租户前缀及尾部活动租户/Host 请求，跨轮次仍能处理尾部。
- 租户重新启用后，前缀可再次被发现；范围损坏、解析故障、取消及异常不扩大执行权限。
- 保留工作流关联幂等、批准后业务变更的幂等与版本检查，验证没有因重试游标而漏掉新候选。

## PERF-14：GoView 列表物化全部画布

**优先级：P2。第三轮新增。**

代码位置：

- [GoViewProjectSql.cs](../../src/Modules/Full.NET.Modules.GoView/Persistence/GoViewProjectSql.cs)，第 8–14 行项目和版本投影均含 `CanvasJson`；第 60 行起 `ListProjects`、第 92 行起 `ListVersions` 无分页或行数上限。
- [GoViewProjectQueryService.cs](../../src/Modules/Full.NET.Modules.GoView/Features/ManageProjects/GoViewProjectQueryService.cs)，`ListAsync`、`ListVersionsAsync` 物化全部记录并映射完整响应。
- [GoViewProjectsView.vue](../../ui/admin/src/views/GoViewProjectsView.vue)，列表只展示名称、项目键、版本和启用状态，却接收全部草稿画布。
- [GoViewCanvasValidator.cs](../../src/Modules/Full.NET.Modules.GoView/Domain/GoViewCanvasValidator.cs)按字符串长度校验，[GoViewCanvasPolicy.cs](../../src/Modules/Full.NET.Modules.GoView/Domain/GoViewCanvasPolicy.cs)的长度上限为 `2 * 1024 * 1024`。

单个画布有上限，但项目数量和发布版本数没有读预算。查询列表时会同时读取、映射、序列化并传输全部画布；例如 100 个接近上限的 ASCII 画布，仅原始画布文本就约 200 MiB，尚未计入字符串、JSON 转义和框架分配。项目列表 UI 不使用这些正文；历史版本列表也会一次返回全部快照，且列表入口已经存在单项目/单版本详情接口可供按需读取。这不同于 PERF-03 的浏览器逐页下载：这里一次列表 SQL 就携带全部大正文。

后续处理方向：项目列表及版本历史使用有界摘要投影、服务端分页或稳定游标，详情和预览按需读取单份画布；同步调整公共契约、生成客户端、Vue 及 AOT 映射。保留项目、发布版本和草稿的读取权限差异，避免为了缩减响应而误把草稿替换为发布正文。

待验证场景：

- 多项目、大画布、长版本历史下，列表行数和响应大小均受上限约束，摘要不包含画布正文。
- 项目/版本翻页顺序稳定，单项目、单版本和预览仍返回正确正文及发布版本。
- 双库、生成客户端、Vue 导航和 Native AOT 可达映射覆盖新投影；记录数据库读取字节、响应大小和分配，不能仅比较压缩后的网络包。

## PERF-15：MQTT 逐条建立 Broker 连接

**优先级：P2。第三轮新增。**

代码位置：

- [MqttBrokerPublisher.cs](../../src/Modules/Full.NET.Modules.Mqtt/Infrastructure/MqttBrokerPublisher.cs)，第 30–44 行每次创建 MQTT Client 并连接，第 61–66 行发布后断开。
- [MqttModule.cs](../../src/Modules/Full.NET.Modules.Mqtt/MqttModule.cs)，Publisher 为 Scoped；[MqttMessagePublishService.cs](../../src/Modules/Full.NET.Modules.Mqtt/Features/ManageControlPlane/MqttMessagePublishService.cs)对每次发布直接调用该适配器。
- [MqttBrokerOptions.cs](../../src/Modules/Full.NET.Modules.Mqtt/Configuration/MqttBrokerOptions.cs)，默认每用户每分钟 60 条，限流不等于全实例连接并发预算；Broker 功能需显式启用。

对同一个 Broker 连续发布 N 条不同消息会建立 N 次连接，逐条承担 TCP、可选 TLS、MQTT CONNECT/CONNACK 及断开。多用户各自在速率上限内发布时，仍会按消息量形成连接创建和认证开销；当前适配器没有受控连接复用或实例级在途连接预算。此处能从调用链确认握手次数，不代表已经量化吞吐损失，也不涉及把 MQTT 改作可靠业务事件通道。

后续处理方向：在传输适配层评估受宿主管理的持久连接或有界连接池，明确连接身份、并发发布、重连、取消、故障和关闭资源归属。继续在每次请求中执行主题 ACL、用户限流、载荷及幂等检查，不把共享连接当成授权凭据，也不能通过无界后台队列或降低 QoS 换取速度。

待验证场景：

- 同 Broker、多用户连续发布时，连接/握手次数不再随每条消息增长，实际在途连接和发布受控。
- 覆盖 QoS 0/1/2、断线重连、Broker 变慢、调用方取消及宿主关闭，保持消息结果和幂等语义；结果未知时不能免费重发。
- 保留每次主题及租户 ACL 检查；实测 TLS 开关下的延迟、Broker 连接数和错误率，并明确 SDK 并发能力和部署范围。

## PERF-16：MQTT 历史主题包含查询缺少时间窗

**优先级：P2。第三轮新增。**

代码位置：

- [MqttMessageQueryService.cs](../../src/Modules/Full.NET.Modules.Mqtt/Features/ManageControlPlane/MqttMessageQueryService.cs)，第 28–36 行将主题包装成 `%片段%`，`FromUtc`、`ToUtc` 可同时为空；第 53–61 行分别执行精确计数和分页查询。
- [MqttSql.cs](../../src/Modules/Full.NET.Modules.Mqtt/Persistence/MqttSql.cs)，`MessageWhereClause` 对主题使用 `LIKE @TopicPattern`，时间范围均为可选条件。
- [SQL Server 157_Mqtt.sql](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/157_Mqtt.sql)和 [MySQL 157_Mqtt.sql](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/157_Mqtt.sql)，消息表具有时间排序等普通索引，没有将此类无时间窗包含查询变成有界搜索的设施。

具备消息读取权限的调用者只传主题片段、不传时间范围即可搜索全部历史；每次都先做精确 COUNT，再执行列表 SQL。`PageSize <= 100` 只限制结果行，不能限制计数和包含谓词需检查的数据量。随着发布历史累积，这条入口允许不断增长的数据库工作量；当前静态证据不能替代双库执行计划或宣称具体 IO/耗时。相较审计日志已有的 contains 时间窗策略，本模块缺少同类资源约束。

后续处理方向：为主题包含搜索定义强制且有最大跨度的时间窗，或改用经明确设计的精确/前缀查询；保留稳定错误响应和原有读取权限。若改变索引或检索方案，必须有代表性数据及双库执行计划，不能直接假定给 Topic 加普通索引便解决前导通配符。

待验证场景：

- 主题片段搜索缺失时间、跨度超限或时间反转时有确定行为，拒绝场景不进入无界 COUNT/列表 SQL。
- 双库大历史数据、短时间窗、常见和低命中主题下记录执行计划及逻辑读取，区分返回量与实际扫描工作量。
- 前端或其他调用方传入有效边界，分页、状态/客户端组合过滤及权限保持正确；不能仅提高数据库超时规避问题。

## PERF-17：OIDC 授权撤销事务内广播

**优先级：P1。第四轮新增。**

代码位置：

- [OidcAuthorizationManagementService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageOidcAuthorizations/OidcAuthorizationManagementService.cs)，第 28 行开启事务，第 60 行起撤销关联 token，第 80 行起在事务委托内发布撤销通知。
- [IdentityOidcSessionService.cs](../../src/Modules/Full.NET.Modules.Identity/Oidc/IdentityOidcSessionService.cs)，`RevokeActiveApplicationSessionsByUserAndClientAsync` 读取全部关联活动会话后逐条 UPDATE。
- [IdentitySessionRealtimeDelivery.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostOnlineSessions/IdentitySessionRealtimeDelivery.cs)，第 31 行起逐会话发布，每条最多 3 次，失败间隔 50 毫秒。
- [SignalRRealtimePublisher.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/SignalRRealtimePublisher.cs)，`PublishAsync` 直接等待 `SendCoreAsync`，没有事务提交后延迟发送机制。

OIDC 启用且真实实时发布器工作时，撤销授权会先更新授权、token 及应用会话，再在同一未提交事务内逐条广播。Redis/SignalR 变慢或失败时，持有的数据库连接、准入许可及已更新行锁会继续等待网络发送和重试；会话数量越多，串行 SQL 和发送次数越多。实时通知用于加速客户端退出，却在此阻止权威撤销提交，使相关认证访问也可能等待数据库锁。发布器日志中的“authoritative revoke already committed”并不适用于该调用路径；这是 OIDC 独立事务边界，PERF-12 只覆盖支付证书 HTTP 回源。

后续处理方向：在本地短事务内以有界集合操作完成授权、token、应用会话撤销及必要审计，提交后再执行有时限、受控的实时通知；提交前失败不得提前通知成功。保留按用户/客户端的撤销范围、幂等、会话权威校验和失败关闭，不能把通知成功当作撤销事实，也不为实时加速通知引入 Outbox。

待验证场景：

- 双库覆盖多个关联会话与慢/失败实时发布器，发送发生时数据库事务已提交，权威撤销不被通知重试阻塞。
- 事务回滚不发布成功撤销通知；提交后通知失败仍保持撤销有效并有真实可观测结果。
- 覆盖并发授权、上下文切换、重复撤销和取消，其他用户/客户端会话不受影响；记录锁持有、连接占用、SQL 次数及事务时长。

## PERF-18：Office 转换输出无界内存缓冲

**优先级：P2。第四轮新增。**

代码位置：

- [ExternalHttpDocumentOfficePreviewConversionProvider.cs](../../src/Modules/Full.NET.Modules.Document/Providers/OfficePreview/ExternalHttpDocumentOfficePreviewConversionProvider.cs)，第 58–70 行对不可 seek 的成功响应执行 `CopyToAsync(MemoryStream)`，直到完整读完。
- [DocumentOfficePreviewConversionOptions.cs](../../src/Modules/Full.NET.Modules.Document/Configuration/DocumentOfficePreviewConversionOptions.cs)，只有源文件 `MaxInputBytes`（默认 50 MiB）及转换时限，没有转换输出字节预算。
- [DocumentPreviewTaskRunner.cs](../../src/Modules/Full.NET.Modules.Document/PreviewTasks/DocumentPreviewTaskRunner.cs)，转换返回后才将 PDF 交给文件上传端口。
- [HostFileManagementService.cs](../../src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs)和 [LocalFileStorageOptions.cs](../../src/Modules/Full.NET.Modules.Files/Storage/LocalFileStorageOptions.cs)：上传上限默认 10 MiB，但此校验晚于转换响应的完整缓冲。

HTTP 网络响应通常不可 seek，因此成功转换结果会完整驻留内存。输入大小和 120 秒默认转换时限不能约束输出字节量，Office→PDF 膨胀或异常转换服务可以在期限内返回很大的正文；即使最终 PDF 超过文件上传上限，仍先承担完整读取、MemoryStream 扩容及分配成本，随后才拒绝。该路径不同于 PERF-11 的 OSS 错误正文，也不涉及 PERF-04 的 Webhook 领取策略。

后续处理方向：为转换输出定义明确预算，在读取 Content-Length 和实际流时都验证；使用有界流或有界临时文件支撑后续上传，保留取消与资源所有权。预算应与文件上传策略协调，不能只相信响应声明，也不能为省内存而向调用者返回已经随 HTTP response 释放的流。

待验证场景：

- 本地 HTTP 替身分别返回带长度、分块无长度、虚报长度及超过输出预算的成功 PDF，读取到预算边界即停止并释放资源。
- 合法 PDF 正常上传，超限不先完整缓冲；覆盖中断、取消、慢响应及多任务内存占用。
- 不可 seek 与可 seek 路径的流生命周期正确，任务终态和原有版本/租约检查不变；不能以增大上传限额掩盖输出无界。

## PERF-19：公告受众逐项往返形成长事务

**优先级：P2。第四轮新增。**

代码位置：

- [HostAnnouncementManagementService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementManagementService.cs)，第 27–43 行创建/更新进入事务，随后调用受众校验；第 316 行起 `SyncTargetsAsync` 逐目标 INSERT。
- [HostAnnouncementAudienceValidator.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementAudienceValidator.cs)，第 60–68 行逐用户目录校验，第 84 行起逐机构目录校验，未限制目标数量。
- [AnnouncementContracts.cs](../../src/Modules/Full.NET.Modules.Notifications/Contracts/AnnouncementContracts.cs)和 [Endpoint.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/Endpoint.cs)：目标集合直接进入该服务，未发现目标数量的前置业务预算。

定向公告的每个有效用户至少产生一次权威目录读取和一次目标写入；1000 个目标约为 2000 次 SQL 往返，尚未计入公告本身和详情加载。机构目标同样逐条查询、写入。整个流程置于同一事务，校验阶段持续持有连接，写入后又在串行插入期间延长事务和锁占用；目标列表没有业务数量上限，HTTP 请求体限制不能替代 SQL 工作量预算。此处是公告写路径，不是 PERF-05 的模板语言列表补查。

后续处理方向：先对原始及去重后的输入建立数量预算，在本地写事务外经所属模块批量校验活动用户和机构，再以有界集合 SQL 原子保存公告及目标；明确校验到写入间的状态变化语义，不能为了批量化跳过活动状态、租户归属或权限检查。业务模块不能直接 JOIN Identity/Organization 表。

待验证场景：

- 边界目标数量、超限、重复输入、失效用户和不同租户机构均有确定行为，超限先拒绝而不执行逐项查询。
- 双库 SQL 次数随批次而非目标数量增长，创建/更新全部目标保持原子，版本冲突或写失败不留下部分目标。
- 验证校验阶段无 Notifications 写事务，保留并发停用、取消和输入规范化语义，记录大受众下连接及锁持有时间。

## PERF-20：访问日志页耗尽全部历史游标

**优先级：P2。第四轮新增。**

代码位置：

- [AccessLogsView.vue](../../ui/admin/src/views/AccessLogsView.vue)，第 55 行使用客户端分页，第 99 行等待 `fetchAllLogs`，第 110–129 行循环至 `hasMore=false` 才返回。
- [access-logs.ts](../../ui/admin/src/api/access-logs.ts)，`listAuditingAccessLogsByCursor` 默认 `limit=20`，页面未覆盖该值，也未传入 AbortSignal。
- [HostAccessLogQueryService.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/HostAccessLogQueryService.cs)，单次游标查询已有返回上限，但允许未含 contains 的无时间筛选查询。
- [AuditingContainsTimeRangePolicy.cs](../../src/Modules/Full.NET.Modules.Auditing/AuditingContainsTimeRangePolicy.cs)，无 contains 时不要求时间窗，故默认 `{}` 查询会覆盖全部保留历史。

打开或刷新访问日志页时，浏览器串行下载整个可见历史并积累到数组，全部完成后才赋给表格。100 万条历史按当前默认值需要约 5 万次 HTTP 游标查询；数据库 keyset 避免了深 OFFSET，并不能消除总查询、传输和浏览器内存成本。组件没有对这段循环建立页面离开或新查询的取消边界，已触发的历史扫描还会继续。PERF-03 仅记录用户管理页，这里是独立的高增长日志入口，不能以用户页修复代替关闭本项。

后续处理方向：把日志浏览改为当前页或显式“加载更多”的有界游标消费，默认有明确时间范围；完整导出继续走已有导出入口和预算。建立查询代次及 AbortSignal，切换筛选、刷新和离开页面时停止旧循环，保留游标与筛选绑定、日志读取授权及详情字段裁剪。

待验证场景：

- 大历史量打开页面只请求固定有界数据，不等待 `hasMore=false`；继续翻页才发送下一游标请求。
- 新查询和离开页面时取消旧请求，旧响应不覆盖新结果；空页、末页和时间筛选保持正确。
- 真实 Vue 流程覆盖权限导航、错误页与导出，记录首屏请求数、传输行数及浏览器内存，不能仅把默认 limit 调大。

## PERF-21：机构受众统计拉取全部成员再计数

**优先级：P2。第四轮新增。**

代码位置：

- [TenantOrganizationUserMembershipReader.cs](../../src/Modules/Full.NET.Modules.Organization/HostDashboard/TenantOrganizationUserMembershipReader.cs)，第 45 行起 `CountDistinctActiveMembersAsync` 对每个机构读取全部用户，累积到 `HashSet<Guid>`，最终只返回 Count。
- [OrganizationMembershipSql.cs](../../src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationMembershipSql.cs)，`ListActiveUsersByTenantAndUnit` 无行数上限。
- [HostAnnouncementReadStatsQueryService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/HostAnnouncementReadStatsQueryService.cs)，`ResolveEligibleRecipientCountAsync` 为机构公告调用该计数端口。
- [HostAnnouncementsView.vue](../../ui/admin/src/views/HostAnnouncementsView.vue)，打开阅读统计和回执翻页均经 `loadReadStats` 重新请求统计。

阅读统计需要一个去重人数，但当前为 N 个机构执行 N 次成员列表查询，将所有匹配隶属的用户 ID 传回进程后去重。机构和成员越多，传输、对象创建与 HashSet 驻留越大；多机构重叠成员仍需重复读取。仅 100 个各有 1 万成员的目标，查询返回量即可达到约 100 万条成员投影，而业务最终仅消费一个数字。回执翻页还会重复请求该统计。此项为 Organization 所有者端口内的聚合实现，不同于 PERF-02 的机构参考接口全量显示目录，也不属于 PERF-19 的公告受众写入。

后续处理方向：在 Organization 内用受控目标集合执行去重聚合，返回最小计数；给目标输入和聚合执行建立边界，批量化不得跨模块读表。精确保留“不同目标机构/租户中同一用户只计一次”及活动隶属条件，不能简单把各机构人数相加；界面回执翻页是否需要重取摘要应按一致性需求单独确定。

待验证场景：

- 双库覆盖重叠机构、同用户跨多个目标、重复目标、跨租户目标、活动/停用隶属及空集合，计数与原始集合语义一致。
- 大成员量时端口不再物化全体用户 ID，数据库调用受批次控制，记录返回行数、分配和执行计划。
- 保留 Host 统计读取权限、精确 TenantId/UnitId 约束和当前统计口径；不得通过跨模块 JOIN 或陈旧授权缓存换取速度。

## PERF-22：AI Worker 长任务阻塞心跳，导致准入误拒绝

**优先级：P1。第五轮新增。**

代码位置：

- [AiAgentRunCoordinator.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunCoordinator.cs)，第 39–53 行仅在批次开始写心跳，随后逐个等待运行完成。
- [AiAgentWorkerHeartbeatService.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentWorkerHeartbeatService.cs)，第 24 行将新鲜度阈值定义为 `PollMilliseconds * 3`。
- [AiAgentRuntimeOptions.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRuntimeOptions.cs)，默认轮询 1000 毫秒、批次 4，允许的运行期限默认 300 秒。
- [AiAgentRunReadiness.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunReadiness.cs)，第 23–31 行要求存在匹配版本的新鲜 Worker 心跳。
- [AiAgentRunManagementService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageAgentRuns/AiAgentRunManagementService.cs)，第 56–61 行不满足就绪条件时返回 `AgentRuntimeUnavailable`。
- [AiAgentRunLeaseWatch.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunLeaseWatch.cs)，独立续租仅更新运行租约，不更新 Worker 心跳。

显式开启 `AcceptsNewRuns` 后，单 Worker 执行超过约 3 秒的正常模型或工具任务，就会在批次完成前被准入查询视为过期。任务仍在正常执行，运行租约也可持续更新，但同一时间提交的新 Run 被拒绝；所有匹配版本的 Worker 都忙于长任务时，多实例同样受影响。实际可接收请求的时间窗口因此受单批耗时控制，与配置的运行期限不匹配。这里不是建议放宽运行租约，也不是 PERF-09 的后台逐权限重复读取问题。

后续处理方向：让实例心跳按独立、有界的生命周期调度，使用独立数据库作用域，避免等待运行批次；区分实例存活、运行租约与处理容量。如果需要容量准入，建立明确的队列或容量契约，不能把“忙于正常任务”隐式解释为“实例已失联”。保留运行版本匹配、停机失效、会话复核与租约 CAS；不能通过取消门禁、故障放行或单纯拉长新鲜度阈值掩盖问题。

待验证场景：

- 默认轮询配置下，阻塞模型调用超过 3 秒，保持运行租约有效，验证 Worker 心跳仍推进、新 Run 可按明确准入策略处理。
- 多 Worker 全部忙碌、不同 RuntimeVersion、真实停止及心跳写入失败时，门禁仍能正确识别无可用实例；旧实例不得持续冒充存活。
- 双库覆盖独立心跳与运行续租、提交、取消、会话撤销及宿主停止的竞争；记录心跳间隔和准入拒绝率，确认未共享在途业务事务。

## PERF-23：Jobs 计划派发与整批执行耦合

**优先级：P2。第五轮新增。**

代码位置：

- [JobExecutionHostedProcessor.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs)，第 65–89 行每轮仅派发一次到期计划，随后等待 `ProcessPendingAsync` 返回才进入下一轮。
- [JobExecutionRunner.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs)，`ProcessBatchAsync` 串行等待全部任务，或等待有界并行的全部分组完成。
- [JobsWorkerOptions.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs)，默认 `BatchSize=10`、`MaxConcurrency=1`、`PollMilliseconds=2000`。
- [JobScheduleDispatcher.cs](../../src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleDispatcher.cs)，到期计划转为 Pending 执行记录的唯一调用来自上述轮询。
- [JobScheduleCalculator.cs](../../src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleCalculator.cs)，第 84–92、162–194 行分别处理结束窗口及 Skip/FireOnce Misfire。

默认配置下，一批 10 个各运行 30 秒的任务可使下一次计划扫描推迟约 300 秒，另加数据库及循环开销；轮询配置的 2 秒不能约束这一延迟。即使调高执行并发，下一轮派发仍须等待该批最慢分组完成。其间新到期的其他任务定义尚未生成 Pending 记录；计划随后可能按既有 Skip、FireOnce 或 EndTime 策略跳过、合并或结束。问题是健康运行期间的扫描节奏受任务耗时控制，不是要求改变这些 Misfire 策略。此项也不同于 PERF-04：Jobs 已有整批续租，不能把这里描述成等待执行的租约必然过期。

后续处理方向：让有界到期扫描独立于任务执行批次推进，分别控制派发频率、扫描批次和执行并发；不同循环使用隔离作用域。保持计划领取锁、推进版本、计划与执行记录的原子事务、多实例防重及禁止并发执行规则，避免用无限派发堆积替代当前阻塞。

待验证场景：

- 用可控阻塞任务占用整个执行批次，让另一任务定义的计划到期，确认其 Pending 生成延迟由派发预算约束，而非等待阻塞任务完成；分别覆盖串行与并行配置。
- 双库、多 Worker 同时扫描时，同一计划仍仅生成一次相应执行；覆盖 Skip、FireOnce、EndTime、禁止并发及乐观版本竞争。
- 覆盖积压、取消与宿主停止，记录计划到期至入队延迟、数据库调用频率和队列规模，确认执行租约续期与调度均可独立收敛。

## PERF-24：Kafka 回放持续读取已完成分区

**优先级：P2。第五轮新增。**

代码位置：

- [KafkaReplayService.cs](../../src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaReplayService.cs)，第 100–155 行一次分配全部范围，完成分区只从 `remaining` 移除，不暂停或取消该分区分配。
- 同文件第 109–130 行先将任意非空消息的 `idlePolls` 清零，再跳过已完成分区；这些消息不增加 `scanned`。
- [KafkaRangeReplayOperationsService.cs](../../src/Modules/Full.NET.Modules.Messaging/Features/ReplayKafkaRange/KafkaRangeReplayOperationsService.cs)，HTTP 运维入口另有链接执行期限。
- [KafkaReplayOperationsOptions.cs](../../src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaReplayOperationsOptions.cs)，API 同步上限默认 1000 条，执行期限默认且最多 45 秒。

当某分区先完成冻结范围、继续接收实时流量，而另一个分区暂时不能返回目标消息或 EOF 时，回放消费者仍不断读取前者的新消息，再在应用层丢弃。它们既不计入 `MaxMessages`，又反复重置“30 秒无进展”计数，因此有效范围没有推进时，读取、分配及 Broker 流量仍可持续消耗。HTTP 入口已有最多 45 秒的外层取消期限，本项不表示 HTTP 请求可以无限执行；风险是在这段预算内处理了与回放范围无关、且不受消息数量上限约束的流量，局部无进展检测也失效。此项与 PERF-06 的常驻消费组启动阻塞无关。

后续处理方向：完成分区后立即暂停或移除其消费分配，只让待完成范围继续取数；将无进展判定绑定到实际范围推进，必要时为物理读取建立独立预算。保留冻结终点、原回放结果计数、Inbox 幂等、审计及取消语义，不能把范围外消息提交给业务处理器或推进常驻消费组位点。

待验证场景：

- 使用可控消费者让分区 A 完成后持续返回范围外新消息、分区 B 暂无结果，验证 A 不再被消费，B 无进展时仍按预算终止。
- 覆盖 EOF、紧凑主题的位点空洞、处理拒绝、重复消息、数量上限与取消；回放结束位点和计数不得被实时流量扩大。
- 验证 HTTP 执行期限仍生效，在 Kafka 双分区持续生产场景记录实际读取数、范围内处理数及流量；涉及业务落库时保持 SQL Server/MySQL 的 Inbox 幂等行为。

## PERF-25：Git 发布无期限占用代码生成工作区锁

**优先级：P1。第六轮新增。**

代码位置：

- [CodeGenerationApplyService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs)，第 118–130 行先获取工作区 Gate；第 305–323 行用 `CancellationToken.None` 等待 Git 发布，结束后才释放 Gate。
- [CodeGenerationRollbackService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRollbackService.cs)，第 466–469 行的回滚后发布也使用 `CancellationToken.None`，仍在工作区 Gate 内执行。
- [CodeGenerationGitWorkspaceService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Git/CodeGenerationGitWorkspaceService.cs)，`PublishAsync` 依次等待 status、add、commit、push；没有为命令建立独立执行期限。
- [ProcessCodeGenerationGitCommandRunner.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Git/ProcessCodeGenerationGitCommandRunner.cs)，第 57–73 行仅依赖传入令牌终止进程并等待退出；无内部超时。
- [CodeGenerationApplyGate.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyGate.cs)，本地独占信号量可叠加数据库会话锁；[DapperDatabaseSessionLock.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperDatabaseSessionLock.cs) 的 Lease 持有数据库连接直到释放。

开启代码生成 Git 与 Push 后，远端 push 不返回，或 commit 所调用的 Git Hook 长时间不退出，会使已经完成本地变更的 Apply/Rollback 一直等待发布。请求断开也不会取消这个阶段；工作区锁长期不释放，后续 Apply/Rollback 持续返回 Busy。启用分布式 Gate 时还会长期占用锁连接并阻挡其他实例。当前代码没有提供应用自身的执行时限；实际命令仍可能因 Git、网络或外部进程自己的限制退出，因此不能把每次网络故障都描述成永久挂起。此项不同于 PERF-10 的检查点清理正文读取。

后续处理方向：为发布阶段与各 Git 命令建立独立、有界、可在宿主停止时收敛的执行预算。本地变更成功后的收尾不能只机械换成已取消的请求令牌；超时后应终止并等待整个进程树退出、收敛管道读取，再释放工作区与数据库会话锁，避免后台 Git 仍在写盘时允许下一次变更。保留本地变更结果和发布失败/结果未知语义，不因推送超时回滚已提交的工作区，也不能自动重做不确定的远端发布。

待验证场景：

- 用可控 Git 命令挂起 push 或 Hook，验证预算到期后命令及子进程退出、Apply/Rollback 返回确定的本地结果与发布状态，后续请求可以重新取得 Gate。
- 覆盖客户端断开、宿主停止、进程已退出及取消竞争，确保不遗留管道读取或工作区写入任务；不得提前释放锁。
- 双库启用分布式 Gate，检查超时后锁连接释放、跨实例互斥仍有效；记录锁持有时间、Busy 持续时间和连接占用。

## PERF-26：JSON 用户导入缺少行数预算

**优先级：P2。第六轮新增。**

代码位置：

- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs)，第 120–150 行的 `/import` 接收 `ImportHostUsersRequest.Rows`，未校验行数；相邻 `/import-file` 通过工作簿解析器进入相同应用服务。
- [HostUserWorkbookCodec.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserWorkbookCodec.cs)，第 19 行及解析逻辑明确限制工作簿最多 1000 行。
- [HostUserManagementService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserManagementService.cs)，第 146–227 行按全部输入分配结果集合、逐行调用 `CreateAsync`；第 50 行进入每行事务，第 334 行执行密码散列。
- [IdentityUserManagementContracts.cs](../../src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs)，`ImportHostUsersRequest` 的注释要求调用方限制行数，但契约和上述入口没有落实该边界。

具有导入权限的调用方直接提交 JSON，可绕过工作簿的 1000 行限制。大量有效新用户会在同一 HTTP 操作内串行执行密码散列、用户校验和写入事务，并保留完整逐行结果；CPU、数据库往返及响应体随输入行数增长。HTTP 宿主或反向代理的请求体限制可能约束总字节数，但不能代替业务行数和昂贵操作预算。本项是批量写入入口的资源边界，不是 PERF-03 的用户列表全量下载，也不要求降低密码散列强度。

后续处理方向：在共同应用边界统一校验单次导入行数，使 JSON、工作簿及内部调用遵守同一预算，超限必须在首笔写入及密码散列前拒绝；确需更大导入时使用有进度、取消与容量控制的明确批次能力。保留逐行成功/失败结果、Host 作用域、超级管理员禁止导入及资料字段授权，不通过无界并发加速散列，也不把全部导入改成一个长事务。

待验证场景：

- JSON 与工作簿分别覆盖边界行数及超限，超限时确认没有创建用户、执行密码散列或启动逐行事务；内部服务调用同样受限。
- 合法批次覆盖重复用户名、无效行、资料字段拒绝、超级管理员类型与取消，逐行结果语义保持一致。
- 双库在允许的最大批次下记录散列 CPU、数据库调用数、请求时长和响应大小；记录实际容量，不把“增加上限”视为优化验证。

## PERF-27：站内信实时提示放大页面查询并发

**优先级：P2。第六轮新增。**

代码位置：

- [NotificationRealtimeDelivery.cs](../../src/Modules/Full.NET.Modules.Notifications/NotificationRealtimeDelivery.cs)，第 37–57 行发送一条站内信时，先推送 MessageReceived，再推送 UnreadCountChanged。
- [realtime.ts](../../ui/admin/src/notifications/realtime.ts)，第 86–94 行两类消息都会递增 `inboxRevision`；其一个在途请求加一个待刷新标记的合并机制仅保护徽标未读数加载。
- [InboxMessagesView.vue](../../ui/admin/src/views/InboxMessagesView.vue)，第 151–153 行每次修订变化直接 `void load()`；第 223–263 行不检查已有在途加载，每次先请求未读数，再请求当前分页，未建立查询代次或取消边界。
- [inbox-messages.ts](../../ui/admin/src/api/inbox-messages.ts)，列表及未读数读取均已支持 `AbortSignal`，页面未传入。
- [MyInboxQueryService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/MyInboxQueryService.cs)，每次分页读取仍包含总数与列表 SQL；多次重复加载会扩大相应数据库工作。

站内信页保持打开时，持续到达的提示会反复启动完整加载链。两类消息若跨 Vue 调度周期到达，同一站内信也可触发两次页面加载；同一周期的响应式批处理可能合并通知，不能据此宣称每条消息必定产生固定数量的请求。网络或数据库变慢时，前一次加载尚未完成，后续提示仍可继续叠加在途请求，`loading` 只控制显示，没有并发上限。壳层已有的徽标合并不能保护这段页面查询；多个重叠结果还可能按完成顺序覆盖当前页。这与 PERF-20 的历史游标全量扫描是不同的放大路径。

后续处理方向：为页面实时刷新建立有界合并，例如一个在途加载与一个待刷新标记；对密集持续提示定义合理刷新频率，避免合并后仍无休止地立即重查。按页码、筛选和会话作用域冻结查询代次，使用现有 AbortSignal 取消过期请求，并在离开页面或上下文切换时收敛。未读数仍使用当前授权作用域的 HTTP 权威值，不能直接信任实时载荷来省略授权读取；保留重连后的最终刷新。

待验证场景：

- 延迟 HTTP 响应，跨多个调度周期连续发送两类提示，检查最大在途加载数和合并刷新次数受限，最终页面能反映最后状态。
- 覆盖持续消息流、已发送页、分页及筛选切换、旧响应迟到、组件卸载、租户切换和会话撤销，旧请求不再覆盖新作用域结果。
- 真实 Vue 与服务端流程记录每条/每批提示对应的列表和未读数请求数、数据库调用及首屏交互延迟；徽标已有的合并与重连恢复行为保持有效。

## PERF-28：签名认证 Nonce 缺少过期回收

**优先级：P2。第七轮新增。**

代码位置：

- [SignatureAuthenticationService.cs](../../src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationService.cs)，第 243–257 行在验签成功后为请求插入 Nonce，并计算 `ExpiresAtUtc`。
- [SignatureNonceSql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/SignatureNonceSql.cs)，仅提供插入与存在性查询，没有删除语句。
- [SQL Server 迁移 042](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/042_IdentitySignatureNonce.sql)及 [MySQL 迁移 042](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/042_IdentitySignatureNonce.sql)，均建立过期时间索引，但未提供自动回收机制。
- [IdentityModule.cs](../../src/Modules/Full.NET.Modules.Identity/IdentityModule.cs)，注册了认证事件及 OIDC 保留清理，没有 Nonce 清理处理器；对 `src/`、`scripts/` 的表名引用核对未找到其他回收实现。

每个通过签名验证的新请求都会持久化一行，`ExpiresAtUtc` 到期本身不会删除记录。当前仓库提供的运行链没有推进该表回收，数据量因而取决于累计请求数，而非有效防重放窗口。作为规模示例，持续每秒 100 次成功验签每天会新增约 864 万行；这只是行数推算，不是已测吞吐。失效记录会长期占用表、唯一索引和过期索引空间，并扩大备份及维护量；具体写入延迟、IO 和索引维护影响尚未实测。此项不是 PERF-08 的 OIDC Token 保留清理。

后续处理方向：在 Identity 所属 Worker 内建立有批次、运行预算、取消和积压观测的过期回收，复用已有 `(ExpiresAtUtc, Id)` 访问路径，双库保持一致。删除边界必须使用已经持久化的到期时间及协议允许的时钟偏差，不能提前删除仍可能用于有效时间窗口的防重放记录；保留验签后的原子唯一插入，缓存未命中不得取代数据库去重。

待验证场景：

- 双库准备大量已过期与未过期 Nonce，单轮删除数量和调用次数受预算限制，多轮能排空过期积压，未到期记录保留。
- 并发相同签名请求仍仅允许一次通过；覆盖时间窗口边界、允许的未来时间戳、多个 Worker 清理及清理与新插入竞争。
- 覆盖取消、数据库失败和恢复，记录表增长、最老过期年龄、清理吞吐、锁等待及写入延迟，不以单条无界 DELETE 替代缺失的回收流程。

## PERF-29：OAuth 登录请求承担无界过期清理

**优先级：P2。第七轮新增。**

代码位置：

- [OAuthFlowService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/OAuthFlow/OAuthFlowService.cs)，第 84–88 行的 `BeginAuthorizeAsync` 在插入新 State 和返回授权重定向前等待全部过期 State 删除；State 默认有效期为 10 分钟。
- [OAuthAuthorizationStateSql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/OAuthAuthorizationStateSql.cs)，`DeleteExpired` 只有 `ExpiresAtUtc <= @NowUtc`，无批次或行数上限。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/OAuthFlow/Endpoint.cs)，`/api/v1/identity/oauth/{providerKey}/authorize` 的有效登录发起进入上述同步链。
- [SQL Server 迁移 151](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/151_IdentityOAuthProvider.sql)及 [MySQL 迁移 151](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/151_IdentityOAuthProvider.sql)，已提供到期访问索引，不能据此推断删除工作量已有边界。

用户发起正常 OAuth 登录或绑定时，要先为此前所有未完成、已过期授权状态做清理。授权流量高峰后留下大量未回调 State，或长期无新发起请求时形成的过期积压，会由后续请求同步删除；并发发起还可能同时尝试删除相同范围。索引能够帮助定位过期数据，但无法限制单次删除的日志、锁及执行时长，登录重定向延迟因此受历史积压影响。这与 PERF-08 的后台 OIDC Token 清理是不同的表、入口与等待链。

后续处理方向：把过期 State 的主要回收移到有界后台清理；如果请求侧仍保留机会性回收，也必须限制其工作量和执行预算。登录安全继续依赖回调中的 State 存在、过期、使用及 PKCE/nonce 校验，不能依赖“过期行已物理删除”才能拒绝请求；双库保持同一保留边界，不把无界删除包装为异步任务留在请求之后。

待验证场景：

- 大量过期 State 积压时，新的有效授权请求不等待全量清理，后台每轮工作受预算限制并最终收敛。
- 双库、多实例并发发起和清理时，未到期 State 不被删除，合法回调、过期回调及重复回调继续符合当前安全语义。
- 记录积压前后授权重定向延迟、删除行数、事务日志及锁等待；覆盖后台失败、取消和停机，不以增加命令超时掩盖无界工作量。

## PERF-30：外部 OIDC 元数据无法跨登录复用

**优先级：P2。第七轮新增。**

代码位置：

- [OAuthFlowService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/OAuthFlow/OAuthFlowService.cs)，第 114、193–195 行分别在授权发起与回调阶段读取发现文档。
- [HttpOidcClient.cs](../../src/Modules/Full.NET.Modules.Identity/OAuth/HttpOidcClient.cs)，第 21–29 行每次 `GetDiscoveryDocumentAsync` 都执行 HTTP GET；第 134–140 行每次验证 ID Token 都新建 `ConfigurationManager<OpenIdConnectConfiguration>` 并等待其加载配置。
- [IdentityDomainServiceCollectionExtensions.cs](../../src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceCollectionExtensions.cs)，`IOidcClient` 虽注册为 Singleton，上述配置管理器仍是单次验证的局部对象。

同一 Authority 的连续登录会反复读取相同发现文档，回调还在令牌交换后建立新的配置管理器读取验证配置，无法复用先前登录已经取得的配置状态。静态 `HttpClient` 复用了连接资源，但没有在应用层合并这些元数据加载。并发登录会重复依赖 IdP 元数据读取的延迟与可用性；额外配置加载仍在返回登录结果前串行等待。本项针对外部 IdP 的元数据读取生命周期，区别于 PERF-17 的本地授权撤销及 PERF-29 的数据库过期清理；实际 HTTP 次数和收益需用受控 IdP 测量。

后续处理方向：按受信 Provider 配置版本及 Authority 管理有界的元数据/签名公钥读取生命周期，合并相同来源的刷新，并按现有缓存与协议规则明确新鲜度、刷新失败及密钥轮换行为。不得缓存 ID Token 的验证结论，不得放宽 issuer、audience、lifetime、nonce、Provider 启停或绑定授权校验；未知签名密钥的刷新与重试必须有界，配置变更和删除应使旧读取状态失效。

待验证场景：

- 同一 Authority 的顺序及并发登录，在规定的新鲜度窗口内发现文档和验证配置读取次数受控；不同 Authority、Provider 版本及配置删除不错误复用。
- 受控 IdP 覆盖密钥轮换、未知 kid、过期元数据、刷新超时和取消；非法签名、错误 issuer/audience、过期令牌及 nonce 不匹配仍拒绝。
- 记录每次完整登录的元数据请求数、并发回源数及尾延迟，披露实际刷新/故障策略；本地会话撤销与租户隔离不受元数据复用影响。

## PERF-31：打印模板选择与版本目录携带全部正文

**优先级：P2。第七轮新增。**

代码位置：

- [PrintingTemplateSql.cs](../../src/Modules/Full.NET.Modules.Printing/Persistence/PrintingTemplateSql.cs)，第 8–14 行模板和版本列均包含 `LayoutHtml`；第 60、101 行的两个列表无分页或数量上限。
- [PrintingTemplateQueryService.cs](../../src/Modules/Full.NET.Modules.Printing/Features/ManageTemplates/PrintingTemplateQueryService.cs)，`ListAsync`、`ListVersionsAsync` 全量物化并映射列表；版本列表还先读取包含草稿正文的模板记录判断存在性。
- [PrintingTemplateMapper.cs](../../src/Modules/Full.NET.Modules.Printing/Features/ManageTemplates/PrintingTemplateMapper.cs)，两个列表 DTO 均携带 HTML 正文。
- [PrintingFormSchemaCatalog.cs](../../src/Modules/Full.NET.Modules.Printing/Domain/PrintingFormSchemaCatalog.cs)，单个布局允许最多 64 Ki 字符，该限制不约束模板或历史版本总数。
- [PrintingPreviewView.vue](../../ui/admin/src/views/PrintingPreviewView.vue)，第 80 行打开页面就取得全部模板，选择与过滤主要使用名称、启用状态和发布版本号；真正预览另走按 ID 的服务端预览入口。

模板数量增长后，打开选择页需要读取、序列化、传输和保留所有草稿 HTML；查看历史目录同样一次返回全部版本正文。1000 个均达到布局上限的模板，仅 HTML 即约 6554 万字符，尚未计入 DTO 与序列化开销；这只是上限规模示例，未作为实际包体或内存测量。单项 64 Ki 字符校验无法约束集合规模。此项是 Printing 自有入口，修复 PERF-14 的 GoView 项目列表不会覆盖它。

后续处理方向：提供有界、可筛选的模板及版本摘要读取，选择器只取所需投影，正文在编辑或预览具体模板/版本时按需读取；不要先全量加载再在浏览器分页。保留 Host 读取权限、模板启停、已发布版本不可变性及表单绑定授权，避免把草稿正文与已发布预览混用。

待验证场景：

- 双库准备大量模板与历史版本，列表返回量有上限且不携带 LayoutHtml，详情及指定发布版本仍返回正确正文。
- 真实 Vue 覆盖打开选择页、搜索与继续加载、创建发布后刷新及按 ID 预览，检查请求行数、传输量与浏览器内存。
- 覆盖禁用、未发布、版本不存在和权限拒绝，模板摘要与预览权限保持一致；记录数据库返回字节、服务端分配及首屏延迟。

## PERF-32：工作流图递归缺少规模和深度预算

**优先级：P1。第八轮新增。**

代码位置：

- [WorkflowModels.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowModels.cs)，`WorkflowDefinitionDraft.Nodes` 是平铺节点集合，模型没有规模限制。
- [WorkflowDefinitionManagementService.cs](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionManagementService.cs)，`CreateAsync` 和 `UpdateDraftCoreAsync` 直接序列化并保存草稿，第 427–433 行发布时反序列化并同步调用编译器，没有先执行节点数、边数或路径深度预算检查。
- [WorkflowDefinitionCompiler.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowDefinitionCompiler.cs)，第 122、378–410 行的环检测通过局部函数 `Visit` 递归访问后继，深度随最长链增长；编译接口没有取消令牌或工作量预算。
- [WorkflowRuntimePlan.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowRuntimePlan.cs)，第 652–691 行路径校验也递归访问后继；已验证状态集合避免重复求解，但不能约束长链的调用栈深度。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/Endpoint.cs)，草稿与发布入口有精确权限，但没有图规模准入约束。

有权设计和发布流程的用户可保存大量节点组成的长链；节点在 JSON 中平铺，增加图的链长度不需要增加 JSON 嵌套深度。发布时同步递归深度由用户图决定，超长合法结构或末端带环结构在返回校验结果前就可能耗尽调用栈，带来 API 进程终止风险；普通请求取消也不能中断这些同步遍历。已有 visited/validated 集合避免的是重复访问，不能消除递归深度。本轮仅确认代码路径与缺失的预算，未执行崩溃试验，也不声明固定节点数必然触发故障。

后续处理方向：在草稿接收及所有编译/运行图读取边界统一限定节点数、边数、配置大小和支持的嵌套/路径深度，超限以稳定错误拒绝；将深图遍历改为显式栈或拓扑算法，并提供可取消的工作量预算。保留环检测、全部节点可达、每条有效路径含审批以及网关配对等现有语义，不得只跳过校验或靠提高线程栈大小掩盖输入无界。

待验证场景：

- 覆盖节点/边/深度预算边界、超长无环链、长链末端成环及大量汇合分支，保证返回受控结果、遍历工作有界且不依赖递归调用栈。
- 覆盖 Host/Tenant 创建、草稿更新、发布及持久化图重新读取；超限输入在昂贵编译前拒绝，不绕过权限与租户边界。
- 用隔离进程进行深图与取消验证，避免在共享测试进程中主动触发栈溢出；记录 CPU、分配和处理时间，双库验证持久化边界，正常图编译产物及审批路径语义保持一致。

## PERF-33：工作流发布为存在性判断枚举全部角色成员

**优先级：P2。第八轮新增。**

代码位置：

- [WorkflowDefinitionManagementService.cs](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowDefinitionManagementService.cs)，第 485–502 行对每个审批节点的每条来源串行执行 `ValidateSourceAsync`。
- [WorkflowAssigneePublishValidator.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowAssigneePublishValidator.cs)，第 93–114 行先查角色，再读取全部活动成员，最终仅判断各角色的 `userIds.Count > 0`。
- [WorkflowRoleMemberDirectory.cs](../../src/Modules/Full.NET.Modules.Identity/Workflow/WorkflowRoleMemberDirectory.cs)，`FindActiveMemberUserIdsByRoleIdsAsync` 全量物化角色成员，再 GroupBy、去重和生成数组。
- [WorkflowRoleMemberSql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/WorkflowRoleMemberSql.cs)，第 120、141 行 Host/Tenant 成员查询返回全部匹配用户并排序，没有行数上限。
- [WorkflowAssigneePolicy.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowAssigneePolicy.cs)，每条来源最多 5 个角色；该限制不约束角色成员数，也不合并不同节点的重复角色。

发布所需事实是“角色有效且至少有一名活动成员”，但当前把该角色的全部成员 ID 传回应用层；多个节点复用同一角色时还会重复读取。以 20 条来源都使用同一个有 1 万活动成员的角色为例，仅成员投影就会读取约 20 万行，并伴随逐来源角色查询与集合分配；这是静态调用量示例，未测实际延迟。此项属于 Workflow 发布前的存在性校验，不是 PERF-21 的公告机构受众去重计数，也不要求移除运行时真正需要的办理人解析。

后续处理方向：由 Identity 所有者端口提供按有界角色集合读取的活动角色/成员存在性投影；发布时汇总去重当前定义引用，再按各来源应用原规则，避免为了一个布尔判断下载全部用户。保留角色活动状态、超级管理员角色排除、Host/Tenant 及成员活动过滤，不允许 Workflow 跨模块 JOIN，也不跨发布请求缓存授权事实。

待验证场景：

- 双库覆盖超大角色与多个节点重复引用，发布所传回行数随不同角色数有界，而非随角色成员总量增长；检查 SQL 次数、执行计划和服务端分配。
- 角色不存在、停用、空成员、仅停用成员、超级管理员角色、不同租户角色及允许的 Host 用户成员关系，发布结论保持一致。
- 保留发布前权威复核和原运行时成员解析语义，成员/角色并发变更不被陈旧缓存掩盖；覆盖取消和无角色来源的正常流程。

## PERF-34：配额基线对账全量执行逐租户聚合

**优先级：P2。第八轮新增。**

代码位置：

- [TenantQuotaUsageBaselineService.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReconcileQuotaUsageBaseline/TenantQuotaUsageBaselineService.cs)，第 45–86 行全量读取缺失租户、逐个补建，再全量读取该指标；第 93–123 行对每个无预留指标串行查询权威用量并按需更新。
- [TenantQuotaSql.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReserveTenantQuota/Persistence/TenantQuotaSql.cs)，`ListActiveTenantsMissingQuotaMetric` 和 `ListMetricsByMetricCode` 均无分页或批次数量边界。
- [TenantActiveMemberCountPort.cs](../../src/Modules/Full.NET.Modules.Identity/HostUsers/TenantActiveMemberCountPort.cs)及 [TenantResourceFileStorageUsagePort.cs](../../src/Modules/Full.NET.Modules.Files/Features/TenantResourceFiles/TenantResourceFileStorageUsagePort.cs)，每次只为一个租户执行 COUNT 或 SUM。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReconcileQuotaUsageBaseline/Endpoint.cs)，HTTP 操作等待整次对账完成；[TenantQuotaContracts.cs](../../src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantQuotaContracts.cs) 的请求仅包含 DryRun 和 MetricCode，没有目标批次或续扫游标。

对账开销随全部租户数增长，即使 DryRun 或所有 UsedValue 都已正确，也要逐租户执行权威聚合。例如 1 万个无预留且无需修改的现有指标，仍需要约 1 万次串行 COUNT/SUM，另加两次候选读取；新补建指标又会进入后续全量列表，重复查询其刚读取的权威用量。整个请求还物化全部候选及受影响租户集合，缺少单次工作预算与续扫位置。这里没有声称存在跨模块长事务；实际等待、IO 及锁影响需双库测量。

后续处理方向：把对账改为有界目标批次和可继续推进的扫描，权威模块通过最小 Contract Port 返回有界租户集合的聚合结果；避免新补建行在同一轮重复查询。明确大规模对账的进度、取消及结果获取方式，不能在 Tenancy 跨模块直接读表或用无界并发加速。保留 `ReservedValue = 0`、Version CAS、指标/周期、权威用量及 DryRun 不写入的约束，不能为了吞吐覆盖在途预留。

待验证场景：

- 双库覆盖大量一致指标、全部缺失、部分差异及空集，每次读取和处理规模受预算限制，续扫最终覆盖全部目标，不重复聚合刚补建的指标。
- 覆盖预留非零、对账期间新增预留、UsedValue/Version 竞争、权威模块不可用及 DryRun，仍不覆盖在途状态或把未知用量当成零。
- 覆盖取消与继续处理，记录每批数据库往返、COUNT/SUM 返回量、请求时长及内存；统计和候选结果不要求调用方一次持有全租户数组。

## PERF-35：LDAP 同步网络调用占用请求线程

**优先级：P2。第九轮新增。**

代码位置：

- [DirectoryServicesLdapClient.cs](../../src/Modules/Full.NET.Modules.Identity/Directory/DirectoryServicesLdapClient.cs)，`TestConnectionAsync`、`TestAuthenticationAsync`、`PreviewEntriesAsync` 在返回 Task 前直接执行同步 Bind/Search；第 52、81、100、138 行为实际同步调用，最终使用 `Task.FromResult` 返回。
- 同文件第 113 行设置了 15 秒连接操作超时；取消检查主要位于方法入口及响应映射循环，没有把请求取消连接到在途 Bind/Search。
- [LdapConnectionOperationsService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageLdapConnections/LdapConnectionOperationsService.cs)，三个运维方法直接等待上述客户端，没有独立准入或执行边界。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageLdapConnections/Endpoint.cs)，连接测试、认证测试和同步预览从 HTTP 请求链调用，保留精确测试/预览权限。
- [IdentityAuthenticationServiceCollectionExtensions.cs](../../src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthenticationServiceCollectionExtensions.cs)，客户端以 Singleton 注册；该生命周期本身不限制同时执行的调用数。

慢 LDAP、网络抖动或并发运维请求会让调用线程在原生网络操作中等待，`await` 上层方法及 `Task.FromResult` 都不能把已经发生的同步等待变为异步。认证测试还顺序执行服务账号绑定、用户搜索及用户绑定。已有 15 秒 SDK 超时，因此这里不是“完全没有超时”；问题是多个操作会分别占用线程，请求取消也不能立即结束当前操作，当前客户端没有约束这类在途工作的并发与等待规模。此项适用于这些 LDAP 运维入口，不把它扩大描述为所有登录请求都经过 LDAP。

后续处理方向：搜索优先使用可用的原生异步边界；仅提供同步接口的绑定须纳入有界、受宿主管理的执行边界，明确准入、排队、原生超时、取消和停止排空。不能简单套 `Task.Run` 转移到同一线程池；底层操作尚未结束时，也不能只取消等待就释放仍被使用的连接或执行名额。保持 TLS、凭据保护、搜索 DN 范围及精确运维权限。

待验证场景：

- 用延迟 Bind/Search 的受控 LDAP 服务分别覆盖三个入口，验证最大在途操作和排队受限，慢目录不使请求线程等待数无界增长。
- 覆盖请求取消、宿主停止、超时、绑定失败和搜索失败，停止新派发并最终释放连接；不得遗留无人观察或继续使用已释放连接的工作。
- 记录真实操作时长、线程池等待、在途连接和拒绝率；保留正常认证测试、TLS、凭据不泄露及越界 DN 拒绝行为。

## PERF-36：LDAP 预览上限仅作用于响应映射

**优先级：P2。第九轮新增。**

代码位置：

- [LdapConnectionOperationsService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageLdapConnections/LdapConnectionOperationsService.cs)，默认预览 50 条，最多 200 条；第 113–122 行把钳制后的 `maxEntries` 交给客户端。
- [DirectoryServicesLdapClient.cs](../../src/Modules/Full.NET.Modules.Identity/Directory/DirectoryServicesLdapClient.cs)，第 76–81 行创建 OneLevel SearchRequest 后直接 `SendRequest`，没有使用 `maxEntries` 设置搜索数量或分页控制；第 82–94 行才在已取得的 SearchResponse 内停止映射。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageLdapConnections/Endpoint.cs)，`preview-sync` 将该操作作为有上限的目录预览提供给管理端。

“最多返回 200 条”目前仅限制 DTO 数量，未限制服务器为本次操作搜索/返回的结果或客户端 SearchResponse 的缓冲规模。某个允许 DN 下有大量同级匹配对象时，即使用户只请求 10 条，也会先等待该搜索响应再丢弃多余条目，放大网络、LDAP 端工作和客户端内存。实际响应还可能受到目录服务器自身限额约束，本项不声称所有服务器都会返回整个目录；风险是应用没有落实自身声明的读取预算。仅把 PERF-35 改为异步仍会保留这段多余读取，反之仅限制结果数也不能消除同步 Bind 阻塞。

后续处理方向：把预览预算传到 LDAP 协议读取边界，按所支持的服务器行为使用结果上限或受控分页；若需要标记截断，最多额外探测一条或一个有界页面。明确服务端限额、部分结果和分页结束的处理，不能为填满页面不断扩大读取或绕过已批准 DN 范围；保留预览不写入用户/机构的语义。

待验证场景：

- 受控 LDAP 同一层级有远超 200 条匹配对象时，请求 10/50/200 条均只读取有界结果；检查发出的搜索控制参数、返回条目及客户端缓冲量。
- 覆盖目录服务器限额、部分结果、分页不支持、空结果与取消，不把正常有界截断误报为成功返回完整目录。
- 保留 OneLevel 范围、配置属性白名单及越界搜索拒绝；记录网络字节、响应条目数、内存和预览时长，不能只测试最终 DTO.Count。

## PERF-37：文档 ACL 重写包含无界逐条写入

**优先级：P2。第九轮新增。**

代码位置：

- [HostDocumentPermissionManagementService.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentPermissions/HostDocumentPermissionManagementService.cs)，第 28–39 行只验证文档标识、非空集合及各条目格式，没有集合数量上限；第 41–79 行在同一事务删除原 ACL，再逐条执行 INSERT，最后读取全部新 ACL。
- [DocumentPermissionSql.cs](../../src/Modules/Full.NET.Modules.Document/Persistence/DocumentPermissionSql.cs)，`DeleteByDocument` 整体删除，`Insert` 每次写一条；`ReadPredicate` 和 `CheckAccess` 同时依赖该 ACL 表。
- [HostDocumentContracts.cs](../../src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs)，`SetHostDocumentPermissionsRequest.Permissions` 为整体覆盖集合，未定义条目预算。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentPermissions/Endpoint.cs)，设置权限入口有独立精确权限，但未增加数量边界。

一次 N 条 ACL 重写要在删除旧权限后的事务内执行 N 次串行插入，再物化全部结果。集合规模增长会直接扩大数据库往返、事务持有时间、日志和分配；重复授权维护也会完整重写。HTTP 请求体限制可以约束字节数，但不能代替单文档 ACL 数量和写入预算。具体是否阻塞其他权限读取取决于提供程序与隔离配置，尚未做双库锁等待测量。此项是文档授权矩阵的写入路径，不是 PERF-19 的公告受众写入，也不是 PERF-33 的工作流发布成员查询。

后续处理方向：为单文档 ACL 建立明确条目上限并在删除前拒绝超限输入，在同一事务内用有界集合写入减少逐条往返，并遵守各提供程序参数预算。必须保持整文档替换的原子性和空集合恢复模块权限的既有语义，不能拆成多个独立事务，也不能以无事务内存 diff 换取速度；保留 Host 边界、精确授权和取消回滚。

待验证场景：

- 双库覆盖空集合、最大合法集合及超限，超限时没有删除原 ACL；最大批次的 SQL 次数由集合批次数量决定，而非每条一次。
- 注入写入失败及取消，原 ACL 完整保留；并发读取不观察到部分替换，覆盖授权有/无及各权限级别的访问结果。
- 记录重复重写和大集合的事务时长、数据库往返、日志及锁等待；同时检查结果投影和内存，不能通过删除后分批提交规避长事务。

## PERF-38：MCP 批准事务等待远端网络

**优先级：P2。第十轮新增。**

代码位置：

- [AiMcpRemoteConnectionManagementService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageMcpRemoteConnections/AiMcpRemoteConnectionManagementService.cs)，第 45–49 行 `ApproveToolAsync` 先开启事务；第 229–243 行才在事务回调内等待 MCP 初始化、工具目录及 Schema 读取，随后写入批准记录。
- [DapperCommandTransaction.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs)，`ExecuteAsync` 在进入回调前 `BeginAsync`，回调结束后才提交，连接租约在此期间保持占用。
- [McpClientConnectionManager.cs](../../src/AI/Full.NET.AgenticWeb.Mcp/Client/McpClientConnectionManager.cs)，连接初始化有独立 30 秒期限，但不会让外层数据库事务在网络等待期间释放。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageMcpRemoteConnections/Endpoint.cs)，`approve-tool` 以 MCP 管理权限进入上述路径。

批准一个工具的只读远端探测会持有整个本地数据库事务和连接预算。冷连接初始化慢、远端目录分页多或读取阻塞时，数据库尚无必要写入也持续占用事务资源；并发管理请求会叠加这种连接驻留。这里不是认定所有普通读取锁都贯穿整个网络等待，具体锁行为取决于数据库隔离配置；静态确认的是显式事务及连接生命周期。本项与 PERF-12 的支付证书回源、PERF-25 的 Git 工作区锁等待是独立入口。

后续处理方向：在事务外完成有界远端探测与 Schema 冻结，再用短事务复核连接版本、端点/凭据代次、作用域、启用状态及原批准记录后写入；发生配置竞争时拒绝或重新探测，不能直接使用旧快照。保留只读工具限制、Schema 漂移校验、精确管理权限和租户隔离，不把远端探测移为无人观察的后台任务。

待验证场景：

- 阻塞 MCP 初始化或目录读取，验证等待期间未持有 AI 数据库事务/连接租约；远端完成后批准写入仍原子提交。
- 双库覆盖探测期间连接修改、停用、凭据更新及批准记录竞争，旧 Schema 不得写入新连接代次。
- 覆盖远端失败、超时、取消与数据库写失败，记录事务持续时间、连接占用及准入等待；不得把读取缓存命中作为事务已缩短的唯一证据。

## PERF-39：MCP 工具目录反复全分页读取

**优先级：P2。第十轮新增。**

代码位置：

- [AiMcpRemoteConnectionManagementService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageMcpRemoteConnections/AiMcpRemoteConnectionManagementService.cs)，第 184–201 行发现接口读取全部工具并复制所有 Schema 到响应；第 231–243 行批准单个工具时也先读取完整目录再取目标。
- [McpRemoteToolHandler.cs](../../src/AI/Full.NET.AgenticWeb.Mcp/Client/McpRemoteToolHandler.cs)，第 35 行每次执行一个已批准工具前重新读取全部目录，然后查找该工具、计算 Schema Hash。
- [McpClientConnectionManager.cs](../../src/AI/Full.NET.AgenticWeb.Mcp/Client/McpClientConnectionManager.cs)及 [McpClientRegistration.cs](../../src/AI/Full.NET.AgenticWeb.Mcp/Client/McpClientRegistration.cs)，复用初始化连接，但未给目录遍历定义总页数、工具数或 Schema 总字节预算。
- [Directory.Packages.props](../../Directory.Packages.props) 锁定 MCP 2.2.0。本轮读取其 `ModelContextProtocol.Core` 包内 XML 文档，确认当前 `ListToolsAsync(RequestOptions, CancellationToken)` 重载会聚合全部分页，且每次调用重新获取目录，不在 SDK 内缓存列表结果；该行为不同于只读取一页的请求参数重载。

只需验证一个目标工具时，目标即使位于首屏，当前仍等待整个目录完成。一个有 P 页工具的远端，在连续 K 次工具调用中会形成 K 次全目录读取，即约 K×P 次目录分页请求，另加实际工具调用；这是假定目录页数稳定的调用量推算，不是压测结果。发现接口又把全部 Schema 物化为字符串后返回。外层请求或 Agent 取消仍可终止操作，但连接初始化期限和单次请求超时不能代替整次目录的页数、总量及工作预算。仅修复 PERF-38 的事务边界不会消除这些重复读取。

后续处理方向：使用能实际控制分页和读取量的接口，设置整次期限、总页数、工具数、单 Schema 与累计字节预算；管理端提供有界目录浏览，指定工具校验按受控方式定位目标。传输/SDK 响应边界也须评估，不能在全量读取后才 `Take`。如引入目录复用，必须明确当前批准版本、Schema 漂移、远端列表变更及失效策略，不得省略执行前所需的权威漂移校验或用陈旧目录失败放行。

待验证场景：

- 受控 MCP 远端提供多页大目录，目标位于首屏、末屏或不存在时，读取均受预算限制；检查真实分页请求、总工具数和 Schema 字节，而非只检查最终返回行数。
- 覆盖超大 Schema、分页过多、异常续页、远端延迟及取消，不遗留目录读取任务；发现与单工具批准均返回受控结果。
- 连续执行同一工具时记录目录与工具请求数，Schema 漂移、撤销批准、连接配置变化及分类/出站策略仍失败关闭；不能依靠取消真实工具调用来伪造目录优化。

## PERF-40：知识库 Tenant 成员校验逐人回源

**优先级：P2。第十轮新增。**

代码位置：

- [AiKnowledgeMemberService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeMembers/AiKnowledgeMemberService.cs)，第 46–50 行在 Tenant 分支对每个成员串行调用 `FindActiveTenantMemberAsync`；Host 分支则已有批量查询。
- [AiKnowledgeMembers.cs](../../src/Modules/Full.NET.Modules.Ai/Domain/AiKnowledgeMembers.cs)，单次成员替换最多 100 人，数量上限已存在，本项不是无界集合问题。
- [TenantMemberSelectionDirectory.cs](../../src/Modules/Full.NET.Modules.Identity/HostUsers/TenantMemberSelectionDirectory.cs)，第 53 行起每次调用执行一条成员查询。
- [TenantMembershipSql.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageTenantMembers/Persistence/TenantMembershipSql.cs)，`FindActiveMemberSelectionByUserId` 按可信 TenantId、活动 membership 和活动 Host 账号判定。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeBases/Endpoint.cs)，知识库成员设置进入该应用服务，并以当前主体作为所有者。

合法的 100 人 Tenant 成员设置，需要先完成 100 次串行成员 SQL，再进入 AI 本地写事务；即使所有人都有效，也不能合并往返。此项只针对事务外的跨模块成员校验，后续成员写入另有自己的成本，不把它误述为校验期间已经持有写事务。该路径与 PERF-09 的后台逐权限复核、PERF-33 的角色成员存在性查询不同，修复那些路径不会移除这里的逐人调用。

后续处理方向：由 Identity 的活动成员目录提供有界集合查询，一次或少量批次返回所请求成员的最小有效性投影，再保留 AI 内的所有者与版本约束。不能直接换用不同口径的 Tenant 用户选择目录：这里要求 `fn_identity_tenant_member` 中有效成员关系及活动 Host 账号，不能用“拥有租户角色”替代正式成员状态。保持 100 人上限、空集合、所有者排除和事务外跨模块读取，不做无界并发查询或跨请求授权缓存。

待验证场景：

- 双库分别覆盖 0、1、100 人，成员校验 SQL 次数按有界批次控制，不再随人数逐条增长；记录往返与设置延迟。
- 混合活动/停用成员、停用账号、其他租户成员、只有租户角色却无有效成员关系的用户，仍按原目录语义拒绝；超限、重复及所有者输入继续失败。
- 覆盖成员状态竞争、知识库版本冲突、权限撤销和取消，集合查询不扩大租户范围，失败时不提交部分成员替换。

## PERF-41：公告接收查询全量物化与参数膨胀

**优先级：P1。第十一轮新增。**

代码位置：

- [MyHostAnnouncementQueryService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageMyHostAnnouncements/MyHostAnnouncementQueryService.cs)，第 24–35 行先加载全部可见公告，再在内存筛选、排序和分页；第 73–75 行未读统计复用同一全量加载；第 100–108 行在检查当前用户机构隶属之前，先以全部机构公告 ID 读取目标集合。第 125–130 行查看单条 All/Users 公告也重读全部直接受众候选，只用于判断目标 ID 是否存在。
- [AnnouncementReadReceiptSql.cs](../../src/Modules/Full.NET.Modules.Notifications/Persistence/AnnouncementReadReceiptSql.cs)，第 8–18 行公共投影包含 Content；第 26–63 行两条候选 SQL 均不分页，机构候选读取所有已发布 Host 机构公告，尚未按当前用户隶属过滤。
- [AnnouncementTargetSql.cs](../../src/Modules/Full.NET.Modules.Notifications/Persistence/AnnouncementTargetSql.cs)，第 8–27 行两条目标查询使用 `IN @AnnouncementIds`；[DapperAotEnumerableParameterExpander.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotEnumerableParameterExpander.cs)，第 53–64 行按集合元素逐项展开参数，没有替该查询分批。
- [HostAnnouncementRecipientGuard.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageMyHostAnnouncements/HostAnnouncementRecipientGuard.cs)，第 51–70 行在候选和目标全部加载后，才通过 Organization Contract Port 获取当前用户的活动隶属并过滤。
- [MyHostAnnouncementsView.vue](../../ui/admin/src/views/MyHostAnnouncementsView.vue)，第 146–154 行每次加载并行请求列表和未读计数，分别触发上述全量读取。

即使只取第一页或一个未读数字，也会读取全部直接受众公告及全部机构公告正文，加载机构目标后再裁剪。正文传输、行物化和列表排序成本随历史公告增长，而非随 pageSize 增长；页面同时查询列表和计数会重复承担候选加载。更严重的是，已发布的 Host 机构公告达到 2101 条时，目标查询会把 2101 个公告 ID 一次展开，超过 SQL Server 的 2100 参数上限。该集合在判定当前用户是否有机构隶属前形成，因此即使用户不属于这些机构，也会走到该查询。此为静态调用链及参数数量推导，尚未用真实数据库复现；MySQL 不适用这一相同参数阈值，但仍存在全量读取成本。

本项针对接收端分页、未读和详情读取，不是 PERF-19 的受众设置写事务或 PERF-21 的发布端成员计数；修复 PERF-02 的机构用户参考查询也不会改变此处的公告 ID 集合。

后续处理方向：先经 Organization 最小只读端口取得当前用户的可信活动隶属，再在 Notifications 自有表内按一致受众条件执行有界列表和标量计数；列表使用不含正文的投影，详情按目标 ID 定点验证。相关集合传递遵守提供程序参数预算，避免只分批 IN 却继续物化全部历史正文。不得跨模块 JOIN Organization 表、按客户端传入机构放宽访问，或用过期成员缓存代替权威可见性判定；保留 Host 范围、已发布状态、受众去重及已读/标题筛选语义。

待验证场景：

- 双库准备超过 2100 条机构公告及较大正文，覆盖有/无活动隶属的用户；第一页、未读计数和单条详情均不再发出超预算集合，也不读取全部历史正文。
- 混合 All、Users、不同租户机构受众和重复目标，保持列表、计数、详情结果一致；覆盖成员停用/移除、公告撤回及无权访问，不能先分页再过滤造成漏页或错误总数。
- 检查页面一次加载的真实 SQL、参数数目、返回行/字节和分配，记录同规模延迟与执行计划；取消或查询失败时不返回未经完整授权的候选。

## PERF-42：通知投递列表逐行加载重试历史

**优先级：P2。第十一轮新增。**

代码位置：

- [NotificationDeliveryService.cs](../../src/Modules/Full.NET.Modules.Notifications/Features/ManageDeliveries/NotificationDeliveryService.cs)，第 29–48 行按作用域计数和分页读取；第 49–53 行逐行串行调用 `MapAsync(..., includeReceipts: false, ...)`，但第 158–162 行无条件补查重试记录，仅回执读取受到 includeReceipts 控制。
- [NotificationPlatformSql.cs](../../src/Modules/Full.NET.Modules.Notifications/Persistence/NotificationPlatformSql.cs)，第 390–400 行 `ListAttemptsByDelivery` 每次只接受一个 DeliveryId，读取该投递的重试历史并排序。
- [NotificationDeliveriesView.vue](../../ui/admin/src/views/NotificationDeliveriesView.vue)，第 83–91 行加载列表，第 108 行选择后再查询详情；第 397–409 行重试历史展示使用详情对象，列表表格并不使用该历史。
- [NotificationDeliveryWorkerOptions.cs](../../src/Modules/Full.NET.Modules.Notifications/Execution/NotificationDeliveryWorkerOptions.cs)，MaxAttempts 默认 8、有效范围 1–16；本项保留已有重试预算，不将其误述为 Worker 无界自动重试。

有 N 行投递的列表请求需要 2+N 次串行查询：计数、分页，再对每行加载重试历史。合法的 100 行页面因此执行 102 次查询，即使所有投递都尚无重试记录也会逐行查询；有历史时还会增加返回行、DTO 分配和响应字节。选中某条后又通过详情接口读取其历史。以上是代码路径的查询次数推导，未测量实际延迟。该项属于投递监控列表，不是 PERF-05 的模板语言状态 N+1，也不涉及 PERF-04 的投递租约领取及发送并发。

后续处理方向：按列表实际需要定义精简投影，将重试和回执明细留给详情；若列表契约确需携带历史，则只对本页已按可信作用域筛选出的 ID 做有界集合读取并分组，遵守双库参数预算。不能无界并发执行逐行查询，也不能绕过作用域把其他租户投递历史带入响应；详情保留原有尝试顺序、结果字段、重试版本约束和精确权限。

待验证场景：

- 双库覆盖 0、1、100 行，以及无重试/多次重试记录；列表 SQL 数量不再每行增加，记录查询数、响应字节、分配和同环境延迟。
- 列表仍正确展示状态、修订和时间，详情完整展示按原顺序排列的尝试及回执；重试后列表与详情刷新正确。
- 覆盖 Host/Tenant、其他租户记录、无读取权限、取消和列表/详情读取间状态变化，优化不能扩大可读投递 ID 范围或修改 Worker 租约与重试行为。

## PERF-43：OCR/K3Cloud 响应预算未落实到读取边界

**优先级：P2。第十二轮新增。**

代码位置：

- [PaddleOcrIdCardClient.cs](../../src/Modules/Full.NET.Modules.Ocr/Connectivity/PaddleOcrIdCardClient.cs)，第 46–58 行使用默认完成模式发送请求，完整读取响应字符串后才解析及 `Truncate`；第 50 行失败 Message 仍拼接未截断的 rawJson。第 81 行健康探测也使用默认完成模式，虽只检查状态码，仍等待响应正文缓冲。
- [OcrIdCardPolicy.cs](../../src/Modules/Full.NET.Modules.Ocr/Domain/OcrIdCardPolicy.cs)，MaxImageBytes 为 5 MiB，MaxRawResultJsonLength 为 64 Ki 字符；前者约束源图片，后者仅在上述完整读取后限制保存的 RawJson，不是网络读取预算。
- [K3CloudWebApiClient.cs](../../src/Modules/Full.NET.Modules.K3Cloud/Connectivity/K3CloudWebApiClient.cs)，第 146–157 行所有登录、Save、Submit 共用 `PostAsync`，先默认缓冲响应再 `ReadAsStringAsync`，没有应用定义的响应字节上限。
- [OcrIdCardTaskService.cs](../../src/Modules/Full.NET.Modules.Ocr/Features/ManageIdCardTasks/OcrIdCardTaskService.cs)，第 177–185 行将识别错误 Message 交给失败写入；第 218–225 行按未知外部副作用收敛传输异常。[K3CloudDocumentSyncService.cs](../../src/Modules/Full.NET.Modules.K3Cloud/Features/ManageDocumentSyncs/K3CloudDocumentSyncService.cs)，第 274–280 行对远程异常保留 ProviderUnknown。

Provider 返回体积远超业务需要的成功 JSON、错误页面或健康响应时，当前代码会先承担整段网络缓冲；识别及单据结果还会分配完整字符串和解析对象。OCR 的 64 Ki 字符截断不能降低此前的峰值内存，错误 Message 又保留完整正文。OCR 识别与 K3Cloud 的 HttpClient 已有 30 秒超时，OCR 健康探测为 10 秒，本项不声称完全没有时限；风险是在已有时间内仍没有贴合业务的读取字节预算，且只检查状态的健康探测也下载正文。尚未测量大响应下的内存峰值、GC 或并发影响，不将框架默认缓冲上限称为无限内存。

此项是 OCR 与 ERP 同步适配器的独立读取路径，不是 PERF-11 的 OSS 错误正文或 PERF-18 的 Office PDF 输出；修复那些入口不会改变这两类 Provider 请求。PERF-44 的连接池复用也不能约束响应大小。

后续处理方向：在真正读取正文前采用响应头完成模式，并对成功、失败分别设置可解释的字节预算，按实际读取量检查，不能仅相信 Content-Length 或在读完后截断。健康探测只读取协议所需内容；错误摘要与保留原文各自有界并遵守敏感信息规则。切换读取模式时须把既有超时/取消贯穿整个正文读取，不能让 HttpClient 的期限只覆盖响应头。超限和中途断流必须按是否可能已产生远端副作用收敛，尤其不能把未知 Save/Submit 或可能已计费的 OCR 误作确定失败并自动重发。

待验证场景：

- 受控 HTTP 服务分别返回正常小 JSON、超大成功 JSON、超大错误正文和无 Content-Length 的分块响应，验证读到预算边界即停止，保留原文与错误摘要也受限；健康探测不完整缓冲大正文。
- 覆盖慢速正文、读到一半断流、超时及取消，响应流被释放，整个读取仍有期限；记录真实读取字节、峰值分配、GC 和相同并发下的尾延迟。
- 已提交远端调用在超限/读失败后保持正确未知状态；K3Cloud 已保存的单据标识不得丢失或触发重复 Save，OCR 不新增无条件重试。后续实施涉及状态持久化时成对验证 SQL Server/MySQL。

## PERF-44：OCR/K3Cloud 传输连接池随每次操作销毁

**优先级：P2。第十二轮新增。**

代码位置：

- [PaddleOcrIdCardClient.cs](../../src/Modules/Full.NET.Modules.Ocr/Connectivity/PaddleOcrIdCardClient.cs)，第 29 行识别、第 72 行探测分别 `new HttpClient` 并在方法结束释放，没有复用的 Handler。
- [K3CloudWebApiClient.cs](../../src/Modules/Full.NET.Modules.K3Cloud/Connectivity/K3CloudWebApiClient.cs)，第 29、49、72、88 行每次公开操作创建并释放会话客户端；第 132–143 行每次创建新的 HttpClientHandler、CookieContainer 和 HttpClient。SaveDocument 与 SubmitDocument 各自登录，最多只在各自方法内复用连接。
- [OcrModule.cs](../../src/Modules/Full.NET.Modules.Ocr/OcrModule.cs)，第 48–50 行，以及 [K3CloudModule.cs](../../src/Modules/Full.NET.Modules.K3Cloud/K3CloudModule.cs)，第 43–45 行，将适配器注册为单例，但单例内部仍按上述方法创建客户端；类中 HttpClientName 常量未在生产注册或调用中用于创建受管客户端。
- [K3CloudDocumentSyncService.cs](../../src/Modules/Full.NET.Modules.K3Cloud/Features/ManageDocumentSyncs/K3CloudDocumentSyncService.cs)，`InvokeAndPersistAsync` 顺序执行 SaveDocumentAsync 和 SubmitDocumentAsync，二者之间持久化 Save 结果，不共享 HTTP 会话客户端。

同一 Provider 的连续 OCR 请求无法复用前一次操作的连接池；K3Cloud 一次成功的新单据同步包含两次登录及 Save、Submit，在两个独立 Handler 内执行，后续单据又创建新的池。频繁操作时会重复承担连接建立、可能的 TLS 握手与关闭成本，并增加短命连接周转。这里不把单个 K3Cloud 方法中的登录和业务请求误述为一定各建一条连接，也不声称已经发生端口耗尽；可确认的是连接池所有权随每次方法调用结束，缺少跨操作的受管传输复用。实际连接数、握手数和延迟需受控服务验证。

本项与 PERF-15 的 MQTT Broker 连接属于不同实现及协议；仅限制 PERF-43 的响应大小仍会保留客户端创建与连接周转。

后续处理方向：为 OCR 使用受管、可复用的 HTTP 传输，凭据继续按请求设置；为 K3Cloud 将传输池生命周期与业务 Cookie 会话隔离，避免直接把所有账号接到共享 CookieContainer。复用必须按受控端点、账号/账套及配置/凭据代次处理，定义容量、连接更新、停用/轮换和释放边界，不能建立无界客户端字典或陈旧授权会话缓存。可评估在一次同步内复用隔离会话，但不得省略 Save 结果的持久化检查、扩大事务等待或为连接故障自动重放有副作用的请求。

待验证场景：

- 对支持 keep-alive 的受控 HTTP/HTTPS Provider 连续执行识别、探测及单据同步，记录接受连接、握手、HTTP 请求数与延迟，确认连接复用，而非只断言 DI 对象为单例。
- 同一远端主机配置不同账号/账套、不同 API Key，并发执行时 Cookie 和凭据不串用；覆盖配置停用、密钥轮换、端点变化及受控 DNS 更新。
- 覆盖取消、连接断开、认证过期和应用停止，传输资源有界释放；Save 成功后 Submit 失败的恢复仍仅提交既有单据，保持版本竞争、未知结果、精确权限与原作用域语义。

## PERF-45：审计清理小预算缺少跨轮类别进度

**优先级：P2。第十三轮新增。**

代码位置：

- [AuditingRetentionOptions.cs](../../src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionOptions.cs)，第 24 行默认 MaxBatchesPerRun 为 15，第 67–72 行允许配置 1～100；清理默认关闭，问题仅在明确启用且使用下述小预算时触发。
- [AuditingRetentionRunner.cs](../../src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionRunner.cs)，第 43–49 行类别固定排序 Access、Operation、Exception、Outbound；第 71–79 行每次调用重置 active/batches 并从相同类别开始，第 90–102 行即使删除 0 行也消耗一次批次预算。
- [AuditingRetentionHostedProcessor.cs](../../src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionHostedProcessor.cs)，`ProcessOnceAsync` 每轮新建 DI Scope 调用 Runner，没有保存下一轮应从哪个类别继续。
- [AuditingRetentionTests.cs](../../tests/Full.NET.UnitTests/Auditing/AuditingRetentionTests.cs)，第 180–225 行已有轮内类别轮转测试，但使用 MaxBatchesPerRun=4，未证明小预算跨轮进展。

启用清理并设置合法的 MaxBatchesPerRun=1 时，每轮只访问 Access，Operation/Exception/Outbound 永远得不到清理；预算为 2、3 时，分别有末尾两类、一类永远不执行。即使前面的表已经没有过期数据，空探测仍耗尽相同前缀预算，等待更多轮次也不能恢复后续类别。运维为降低单轮数据库影响而调小合法预算，反而会留下无法被此任务消化的过期历史，持续增加存储、索引和后续维护成本。默认预算 15 不触发这里的完全饥饿，本项不将其误述为默认清理全部失效；上述结论来自控制流推导，尚未运行双库复现。

该问题是四张审计表之间的类别调度，不是 PERF-01 的文件候选行游标或 PERF-13 的无效租户前缀，也不是 PERF-08 的单条无界 DELETE；这些路径分别修复不会让审计类别跨轮轮转。

后续处理方向：在明确的每轮总预算内维护跨 Scope 的类别轮转位置，使每个启用类别在有限轮次获得机会，空探测、失败和取消也有清楚的进度规则。不能通过每类都额外执行一批来悄悄突破总预算，也不能通过缩短保留期或删除未到期数据减少积压；保留各类别独立截止时间、Host 作用域及 SQL Server/MySQL 原有有界删除和锁定语义。

待验证场景：

- 双库分别设置总预算 1、2、3，连续跨 DI Scope 调用；前序表满批、空表及尾部有积压时，所有类别均在有限轮次推进，单轮批次数不超预算。
- 覆盖默认 15、边界预算、某类别查询失败、取消及配置变更，故障不使其他类别长期失去机会，也不删除保留期内记录。
- 记录各类别最老过期记录年龄、删除量和数据库工作量，验证低预算降低单轮压力时仍有跨轮清理进展；不能仅断言总删除行数大于零。

## PERF-46：打印 HTML 预处理重复扫描且无法按请求取消

**优先级：P2。第十三轮新增。**

代码位置：

- [PrintingHtmlRenderer.cs](../../src/Modules/Full.NET.Modules.Printing/Domain/PrintingHtmlRenderer.cs)，第 12–14 行 ScriptTagRegex 使用回溯引擎和 Compiled，但未传 matchTimeout；第 22、40–41 行同步执行 Replace，调用没有取消或工作预算。此处不是第 9 行的占位符源生成表达式。
- [PrintingFormSchemaCatalog.cs](../../src/Modules/Full.NET.Modules.Printing/Domain/PrintingFormSchemaCatalog.cs)，第 6–10 行已有 65536 字符布局上限；[PrintingTemplateManagementService.cs](../../src/Modules/Full.NET.Modules.Printing/Features/ManageTemplates/PrintingTemplateManagementService.cs)，第 217–232 行只检查非空和长度，重复的未闭合 script 起始标签仍满足布局校验。
- 同一 [PrintingTemplateManagementService.cs](../../src/Modules/Full.NET.Modules.Printing/Features/ManageTemplates/PrintingTemplateManagementService.cs)，第 21–30 行创建/更新先进入事务，第 73、116 行在构造写入参数时同步剥离脚本；因此这段 CPU 工作还延长事务及连接驻留。
- [PrintingTemplatePreviewService.cs](../../src/Modules/Full.NET.Modules.Printing/Features/PreviewTemplates/PrintingTemplatePreviewService.cs)，第 60 行渲染已发布布局时再次进入相同表达式；[Endpoint.cs](../../src/Modules/Full.NET.Modules.Printing/Features/ManageTemplates/Endpoint.cs) 已有创建、更新等精确权限，本项不声称匿名入口可直接写模板。

由多个 `<script>` 起始片段组成、没有闭合标签的布局，会让表达式从多个可能起点反复查找尾部闭合标签；Compiled 不会改变这种搜索形态。65536 字符限制控制输入长度，但没有为这段同步计算落实时间预算；请求 CancellationToken 也不能打断正在执行的 Replace。结果是合法长度的异常布局可能占用请求线程，创建/更新期间还占用已开始的数据库事务。该项不同于 PERF-31 的模板/版本全量读取，精简列表投影不能消除这里的 CPU 路径。

本轮局部诊断：Windows 上的 PowerShell 7.6.5、.NET 10.0.11，以源码相同模式及 IgnoreCase/CultureInvariant/Compiled 选项构建 Regex，**仅为诊断额外添加 100ms matchTimeout**；执行一次小输入预热后，对 `'<script>' * N` 分别调用 Replace。未调用项目服务、未打开数据库、未修改生产表达式；结果如下，耗时包含本地异常捕获开销：

| 起始标签数 N | 输入字符数 | 100ms 超时是否触发 | 单次墙钟毫秒 |
| --- | --- | --- | --- |
| 128 | 1024 | 否 | 11.25 |
| 256 | 2048 | 否 | 6.74 |
| 512 | 4096 | 否 | 33.26 |
| 1024 | 8192 | 是 | 120.43 |
| 2048 | 16384 | 是 | 107.04 |
| 4096 | 32768 | 是 | 108.30 |

该诊断仅确认在显著低于现有长度上限的输入中，相同匹配模式也能触及保护期限；不是 Host.Api 端到端基准，不能据此给出原实现的精确耗时、增长倍数或 QPS。原实现没有本次诊断添加的保护期限，尚未执行未受保护的大输入或并发实验。

后续处理方向：采用工作量有界的 HTML 预处理方式，明确畸形标签的处理，不把 Script 剥离当作完整 HTML 安全净化；必要的输入处理在开启写事务前完成，并保留明确超时/失败结果。不能仅把 Compiled 改为另一个标志就视为已消除复杂度，也不能用 Task.Run 加请求超时让后台计算继续运行。保留布局长度、HTML 编码、浏览器白名单净化、精确权限、发布快照和版本竞争语义。

待验证场景：

- 正常脚本、大小写、多个标签、未闭合及大量重复起始片段在现有长度预算内均受控完成；检查空布局、最大合法长度及超限输入，不能靠降低测试输入规模声称问题消失。
- 检查创建/更新输入处理期间不持有写事务，失败不产生部分写入；双库覆盖版本冲突及取消，预览保留编码及前端净化要求。
- 使用受控进程预算验证最大合法输入和并发请求的 CPU、事务时长、取消与尾延迟；局部匹配结果不能替代 Host.Api/Native AOT 受影响路径验证。

## PERF-47：全局限流未在已建立的身份上分区

**优先级：P1。第十四轮新增。**

代码位置：

- [Program.cs](../../src/Hosts/Full.NET.Host.Api/Program.cs)，第 48 行 `UseRateLimiter`，第 50 行才执行 `UseAuthentication`；前面没有为普通 Bearer 请求建立主体的认证中间件。
- [GlobalApiRateLimiterConfigurator.cs](../../src/BuildingBlocks/Full.NET.Hosting/RateLimiting/GlobalApiRateLimiterConfigurator.cs)，第 28–33 行仅在 `User.Identity.IsAuthenticated` 为真时按 NameIdentifier/sub 分区，否则按 RemoteIpAddress；第 36–39 行使用一分钟固定窗口、QueueLimit=0。
- [RateLimitingOptions.cs](../../src/BuildingBlocks/Full.NET.Hosting/RateLimiting/RateLimitingOptions.cs) 及 [appsettings.json](../../src/Hosts/Full.NET.Host.Api/appsettings.json)，全局限流默认启用，默认每分钟 1200 次；该值不是全系统容量证明。
- [GlobalApiRateLimitAssertions.cs](../../tests/Full.NET.IntegrationTests/Api/GlobalApiRateLimitAssertions.cs)，现有真实 API 断言只让同一用户连续耗尽额度，没有对同源不同主体做隔离检查；本轮仅阅读，未运行该测试。

在当前普通 Kestrel/Bearer 请求流程中，限流分区选择发生在认证主体建立之前，即使请求携带有效 Token，也会走来源 IP 分支。同一 NAT 出口的不同用户因此共同消耗每分钟 1200 次的额度，某个用户请求较多会让其他用户收到 429；预期的每主体额度不能生效。若部署前还有未正确区分来源的代理，集中程度可能更高，但本项成立不依赖代理配置错误。此为管道及分区选择的静态推导，未执行多用户真实 HTTP 复现，不将 1200/分钟换算为已测服务吞吐。

该问题属于准入分区与有效吞吐，不是调整限流数值的建议，也与 PERF-22 的 AI Worker 心跳准入误拒绝无关。相关按 User 选键的命名策略后续也须沿实际执行阶段检查，不能只修复全局回调的单元测试。

后续处理方向：明确认证前按可信来源保护昂贵认证的预算，以及认证后按已验证主体执行的业务准入边界，使中间件实际顺序与策略依据一致。不得直接相信 Token 中未验证的 sub 或任意用户 Header，不通过提高额度、移除限流或跳过会话撤销校验掩盖问题；保留匿名登录/会话端点的独立限流、可信代理边界、标准 429/ProblemDetails 和多实例额度范围说明。

待验证场景：

- 真实 Host.Api 将额度设为小值，同一来源 IP 的两个有效用户分别发起请求；一人耗尽自己的主体额度后，另一人的合法额度仍可使用，超限主体得到标准 429。
- 覆盖匿名、无效/撤销 Token、API Key/签名主体、不同来源及可信/伪造转发头，认证前保护仍有效，不允许借伪造主体无限创建可用额度。
- SQL Server/MySQL API 路径均验证会话/租户与精确权限不变，并核对受影响 Native AOT 宿主管道；记录拒绝率与分区行为，不能仅测试手工填充 HttpContext.User 的配置器。

## PERF-48：Webhook 事件入队扇出没有工作量边界

**优先级：P2。第十四轮新增。**

代码位置：

- [WebhookEventEnqueueService.cs](../../src/Modules/Full.NET.Modules.Webhooks/Features/DeliverWebhooks/WebhookEventEnqueueService.cs)，第 26–32 行一次加载全部匹配订阅；第 41–65 行按订阅串行执行 InsertDelivery，每条投递复制相同 PayloadBody，重复投递也逐项进入幂等冲突处理。
- [WebhookDeliverySql.cs](../../src/Modules/Full.NET.Modules.Webhooks/Features/DeliverWebhooks/Persistence/WebhookDeliverySql.cs)，第 8–18 行候选查询没有页数或行数边界，并读取目标地址及保护后的签名材料，入队实际上只使用订阅 ID；第 20–31 行一次 INSERT 一条投递。
- [WebhookSubscriptionManagementService.cs](../../src/Modules/Full.NET.Modules.Webhooks/Features/ManageWebhookSubscriptions/WebhookSubscriptionManagementService.cs)，创建时验证可信租户、URL 和事件类型，但没有对同租户/事件的订阅数量建立预算。
- [WorkflowInstanceCompletedWebhookHandler.cs](../../src/Modules/Full.NET.Modules.Webhooks/Features/DeliverWebhooks/WorkflowInstanceCompletedWebhookHandler.cs)，工作流完成事件直接等待全部入队；[OutboxProcessor.cs](../../src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs)，第 284 行起等待 Handler 返回后才标记原事件完成。

一个事件匹配 N 个活动订阅时，入队至少需要一次全量读取加 N 次串行写入，Handler 完成时间和物化行数随订阅量增长，投递表还保存 N 份载荷。大量同类订阅会把扇出成本带入可靠事件消费链，重复入队尝试也不能跳过逐项工作。当前旧轮询 Handler 本身没有包裹整个扇出的显式本地事务，不能将其误述为默认一直持有一个写事务；若通过事务型 Inbox 消费管道执行，则 Handler 的逐项写入也计入该消费事务。没有实测事务时长、租约丢失或积压速率，本项只登记工作量与往返放大风险。

该项发生在投递记录生成之前，不是 PERF-04 的整批预占发送租约；优化发送阶段不会消除上游全量订阅读取和逐条入队。它也不是 PERF-19 的公告受众设置，两者是不同的应用服务及一致性边界。

后续处理方向：先为单租户/事件的合法订阅规模和单事件扇出定义明确预算，读取只取入队所需投影，采用遵守参数上限的集合写入减少往返。若需要支持超过单次预算的扇出，必须以持久意图和可恢复进度承接剩余工作，不能只 Take 前一批、提前确认原消息后遗漏其余订阅；明确订阅变更时的目标快照语义。保留 EventId/SubscriptionId 幂等、可信租户过滤、取消恢复及至少一次可靠性，不以无界并发代替批量设计。

待验证场景：

- 双库覆盖无订阅、预算内大量订阅和超限创建；检查一次入队的 SQL 次数、读取字段、参数数与载荷写入量，不再每订阅一次数据库往返。
- 原事件重复、部分写入后失败、取消/进程重启和订阅启停竞争时，合法目标均最终有且仅有一个幂等投递事实，不丢尾部目标。
- 分别检查旧轮询及实际启用的 Inbox 消费路径，记录 Handler 时长、积压与事务范围，优化不能改变投递阶段原有租约和出站安全校验。

## PERF-49：AG-UI 空轮询读取未使用的进度明细

**优先级：P2。第十四轮新增。**

代码位置：

- [AgUiStreamService.cs](../../src/AI/Full.NET.AgenticWeb.AgUi/AgUiStreamService.cs)，第 64–78 行每轮先查询归属快照和进度，再取最多 100 条事件；第 113–117 行无事件时等待一秒并继续，非终态空轮次不输出此前读取的 progress。
- [AiAgentRunAgUiReader.cs](../../src/Modules/Full.NET.Modules.Ai/AgUi/AiAgentRunAgUiReader.cs)，第 21–30 行归属快照分别读取 Run 和最大事件序号；第 73–81 行进度分别查询预算和全部步骤摘要；ListEventsAfterAsync 再执行一条事件查询。
- [AiAgentRunStore.cs](../../src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunStore.cs)，第 279–284 行归属读取是一条数据库查询；[AiAgentEventSql.cs](../../src/Modules/Full.NET.Modules.Ai/Persistence/AiAgentEventSql.cs)，第 29–34 行步骤摘要按 RunId 读取全部步骤，第 36–49 行预算查询仅取最近一条。

每条活跃 SSE 在非终态、没有新事件时仍执行五次串行 SQL：Run 归属、最大序号、预算、全部步骤、事件页，然后等待一秒。预算与步骤结果在该空轮次被直接丢弃；并发流数及运行等待时间增长会重复消耗数据库往返和步骤投影。历史事件重放每满 100 条继续下一轮时也会重复读进度。五次是调用链推导，不是压测得到的每秒吞吐；实际轮频还受查询和网络耗时影响，已有事件分页上限不能消除空轮次多余查询。

本项是持久 Agent 事件 SSE 的读路径，不是 PERF-09 的知识索引授权检查，也不是 PERF-27 的站内信页面实时刷新。它不建议减少必要的归属或撤销校验。

后续处理方向：先判断本轮是否需要输出进度，将预算/步骤读取移到确实输出状态的边界，并评估在保持权威归属与游标语义下合并最小读取。较大历史回放应避免每个事件页重读相同进度；任何复用都须定义版本及失效依据，不能把长期缓存的主体/Run 归属当作授权结果。保持断流重放、终态读完尾页、取消释放以及不重跑模型/工具的语义。

待验证场景：

- 双库为 running/等待审批的 Run 保持多个无事件轮次，检查预算与步骤 SQL 不再每轮执行；分别记录无事件、有增量事件及多页历史重放的查询次数和步骤返回行数。
- 完成事件晚于快照、终态超过 100 条历史、断线续传及过期游标仍正确；需要发送状态快照时，预算和步骤与既有一致性约定相符。
- 覆盖归属/作用域不匹配、权限或会话撤销、连接取消及慢客户端，优化不能泄露其他 Run 或遗留轮询任务；记录并发 SSE 下的数据库工作量和尾延迟。

## PERF-50：职位分批导入反复解析同一工作簿

**优先级：P2。第十五轮新增。**

代码位置：

- [ImportExportTaskRunner.cs](../../src/Modules/Full.NET.Modules.ImportExport/ImportTasks/ImportExportTaskRunner.cs)，第 140–175 行每次执行任务的一个批次都重新打开源文件，把文件流、NextLineNumber 和批大小交给 Schema Handler，随后释放流；第 221–222 行按已执行有效行数推进检查点。
- [TenantPositionsStaticImportSchemaHandler.cs](../../src/Modules/Full.NET.Modules.Organization/Features/ImportExport/TenantPositionsStaticImportSchemaHandler.cs)，第 88–103 行每批先调用 ParseImportAsync 解析全部行，再预校验全部行、排序有效行，最后才 Skip/Take 当前批次。
- [OrganizationPositionWorkbookCodec.cs](../../src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositions/OrganizationPositionWorkbookCodec.cs)，第 86–123 行每次解析完整复制文件流、打开 ZIP、验证归档、读取共享字符串和工作表 XML，并解析全部数据行；第 19–24 行已有 1000 行、1 MiB 上传、4 MiB 解压和 2 Mi 字符 XML 上限。
- [ImportExportOptions.cs](../../src/Modules/Full.NET.Modules.ImportExport/Configuration/ImportExportOptions.cs)，默认 BatchSize=50；Runner 将单批钳制为 1～200。本项并非未限制工作簿总行数。

一份 N 个有效行的合法工作簿，以 B 行为一批完成时，会发生约 ceil(N/B) 次完整源文件读取、ZIP/XML 解析及全表预校验，再分别只使用其中一批。1000 个有效行按默认 50 行执行，需要 20 次相同工作簿解析，另有最初的上传预览；批次更小时重复更多。读取、解析和预校验的累计工作量因此约为 N×ceil(N/B) 行，而不是仅遍历 N 行。远端对象存储还会重复回源，但实际流量取决于 Provider 是否有自身复用，本项未测量真实存储、CPU、GC 或任务总耗时。

此项是 Organization 工作簿 Handler 在恢复式 ImportExport 中重复处理同一输入，不是 PERF-26 的 Host 用户 JSON 行数校验缺口，也不是之前已修复的跨租户任务扫描进度。保持当前批次检查点并不能自行消除完整解析。

后续处理方向：将工作簿安全解析与静态预校验结果绑定到不可变源文件身份/摘要、Schema 及版本，建立有界的任务输入读取方式，让后续批次直接读取所需行范围。可评估受控任务内复用或持久化规范化结果，但不能建立无界全局工作簿缓存；跨进程恢复不能依赖内存命中。保持完整归档验证、公式/外部关系拒绝、原始行号、有效行游标及 TaskId/行号幂等，动态授权和成员状态仍在其必要边界重新检查，不能以旧解析结果代替执行授权。

待验证场景：

- 合法 1000 行分别使用批大小 1、50、200，记录真实源文件打开、读字节、ZIP/XML 解析次数、分配及任务耗时；完整解析工作不再随业务批次数成倍重复。
- 混合无效行、重复编码及行号间隙，跨批、恢复、重试与进度提交失败后，执行顺序和行幂等仍正确，不跳行或重复创建职位。
- 源文件身份/内容或 Schema 版本变化、权限撤销、取消、进程重启时拒绝错误复用；原上传、解压、XML 和数据行限额继续生效，业务写入成对覆盖 SQL Server/MySQL。

## PERF-51：系统配置批量写入缺少集合工作预算

**优先级：P2。第十五轮新增。**

代码位置：

- [HostConfigEntryManagementService.cs](../../src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/HostConfigEntryManagementService.cs)，第 61–80 行批量删除和批量更新都先进入单个事务；第 344–374 行删除只检查集合非空，再逐 ID 读取与校验；第 376–379 行一次传递全部 ID 删除。第 392–443 行更新同样只检查非空，然后逐项读取配置、验证值类型及写入。
- [ConfigEntrySql.cs](../../src/Modules/Full.NET.Modules.Settings/Persistence/ConfigEntrySql.cs)，第 209–216 行批量删除使用 `IN @Ids`；第 221–230 行配置值更新每次只处理一个 ConfigKey。
- [ConfigEntryManagementContracts.cs](../../src/Modules/Full.NET.Modules.Settings.Contracts/ConfigEntryManagementContracts.cs)，第 146、150 行两类请求均为集合，没有条目预算；[Endpoint.cs](../../src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/Endpoint.cs)，第 165–205 行以精确删除/更新权限直接调用，未补充数量限制。
- 同一管理服务第 458–461 行将单个值限制为 4000 字符，但该限制不约束集合数量；[DapperAotEnumerableParameterExpander.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotEnumerableParameterExpander.cs) 对集合逐元素展开，不替该删除语句分批。

在所有项目都通过校验的路径中，更新 N 项配置产生 2N 次串行业务 SQL，删除 N 个目标产生 N 次校验加一次集合 DELETE，全部占用外层事务。输入条目数增长直接增加连接驻留、锁等待风险、日志及回滚成本；重复 ID/键也未在入口限制，仍会逐项处理。超过 2100 个删除 ID 时，还存在 SQL Server 集合参数上限风险，而且此前已经执行了逐条校验。HTTP 请求体字节及单值长度上限不能代替集合和数据库工作预算。本项未执行真实双库、参数超限或锁等待复现，不声称已测得具体阻塞时间。

此项属于 Settings 配置管理的写事务，不是 PERF-02 的机构参考读取、PERF-41 的公告候选集合，也不是 PERF-37 的文档 ACL；这些路径分别优化不会改变配置批量 API。

后续处理方向：在开启事务前明确并校验批量条目与总字节预算、重复 ID/键语义及可独立完成的格式检查；事务内以有界集合读取和写入减少往返，并遵守双库参数上限。保留全部成功或全部回滚、受保护保留键、值类型、仅禁用项可删除及更新时版本递增的语义，必要的状态竞争仍须在写边界复核；不能拆成多个独立事务、仅调大超时或无界并发处理来缩短表面等待。

待验证场景：

- 双库覆盖空、最大合法批次、超限和重复输入；超限在开启写事务及逐条查询前被拒绝，合法批次 SQL 数量按有界集合批次数控制，不再每项一查一写。
- 混合保留键、缺失/启用配置、非法类型和值，以及中途失败/取消，原配置完整保留；并发修改状态或版本时不发生部分删除或更新。
- 检查最大集合的实际参数数目、事务时长、数据库往返、日志和锁等待，保留 Host 数据边界、精确权限与标准错误结果；不能只验证返回行数或客户端选择数量。

## PERF-52：交付状态总览逐事件流回源所有权

**优先级：P2。第十六轮新增。**

代码位置：

- [DeliveryStatusQueryService.cs](../../src/Modules/Full.NET.Modules.Messaging/Features/GetDeliveryStatus/DeliveryStatusQueryService.cs)，第 27 行读取一次 Outbox 积压，第 29–35 行按已登记 Topic 循环串行解析每条流的有效交付所有者。
- [EffectiveEventDeliveryOwnerResolver.cs](../../src/Modules/Full.NET.Modules.Messaging/EffectiveEventDeliveryOwnerResolver.cs)，第 30–32 行每次先调用 ownershipStore.FindAsync，命中后经目录解析，缺失时才使用静态目录回退；没有在本次总览请求中合并读取。
- [EventStreamOwnershipStore.cs](../../src/Modules/Full.NET.Modules.Messaging/Persistence/EventStreamOwnershipStore.cs)，第 24–35 行每次 FindAsync 执行单条查询；[EventStreamOwnershipSql.cs](../../src/Modules/Full.NET.Modules.Messaging/Persistence/EventStreamOwnershipSql.cs)，FindByStream 除 CurrentOwner 外还读取切流/回退位置、操作者及原因等完整记录。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Messaging/Features/GetDeliveryStatus/Endpoint.cs)，`/api/v1/messaging/delivery-status` 受 events.read 精确权限保护，每个总览请求都进入该链路。

对 N 条已登记事件流，单次总览包含一次积压读取和 N 次串行所有权 SQL。即使所有流都没有持久化切流记录，也必须先完成各自的空查询才能走静态回退；有记录时又读取总览不使用的完整控制面字段。事件目录扩展或多个运维客户端重复查询时，往返和映射成本随流数累加。Topic 集合来自启动期登记，本项不将其误述为任意客户端可提交的无界集合，也不假设当前页面有固定自动轮询频率；1+N 为调用次数推导，尚未测量真实延迟。

本项针对 Messaging 运维总览，不是 PERF-05 的模板语言状态或 PERF-42 的投递历史 N+1。业务写入前的所有权闸门属于另一条一致性路径，不能为了优化此页而削弱它。

后续处理方向：为总览请求有界读取所需事件键的最小所有权投影，在同一请求内复用快照，再统一应用现有目录解析与缺失回退规则。现有 ListAsync 可作为评估入口，但不能简单改为每次加载所有历史所有权记录及大字段；集合规模和参数仍须有预算。不得用跨请求陈旧缓存决定当前发布所有者，也不得把总览快照当作切流、回退或业务写入时的 Fence 证明；保持 Host 运维授权和单一发布所有权。

待验证场景：

- 双库覆盖多条 Topic、全部无持久记录、部分切流及回退状态，查询次数按有界集合批次控制，不再每流一次；结果仍按 EventType/SchemaVersion 稳定排序。
- 混合静态目录与持久化所有者，覆盖目录限制、配置变化和查询期间的并发切流，保持现有有效所有者解析及失败语义，不返回未授权控制面字段。
- 记录不同流数及并发运维请求的 SQL 次数、返回字节、分配与尾延迟；写入、切流及回退验证仍使用自身权威闸门，不依赖页面读取结果。

## PERF-53：本地 Office 转换临时产物缺少磁盘预算

**优先级：P2。第十六轮新增。**

代码位置：

- [ExternalProcessDocumentOfficePreviewConversionProvider.cs](../../src/Modules/Full.NET.Modules.Document/Providers/OfficePreview/ExternalProcessDocumentOfficePreviewConversionProvider.cs)，第 30–39 行将源文件和 PDF 目标放入工作目录；第 52–86 行启动子进程并设置时限/取消终止，但没有生成期间的文件或目录字节限制；第 102–105 行进程结束后才读取最终 PDF 长度。
- [DocumentOfficePreviewConversionOptions.cs](../../src/Modules/Full.NET.Modules.Document/Configuration/DocumentOfficePreviewConversionOptions.cs)，默认关闭，启用 external_process 后可配置源文件上限、超时和临时根目录，但没有输出/单任务临时磁盘预算；MaxInputBytes 默认 50 MiB、TimeoutSeconds 默认 120 秒。
- [DocumentPreviewTaskRunner.cs](../../src/Modules/Full.NET.Modules.Document/PreviewTasks/DocumentPreviewTaskRunner.cs)，第 82–110 行等待转换完成后才上传 PDF，第 150–161 行在 finally 清理工作目录；第 224 行起默认在系统临时目录下建立独立任务子目录。
- [HostFileManagementService.cs](../../src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs)，第 59–64 行上传会检查输出长度，但此时输出文件已经生成；[DocumentOfficePreviewProcessTests.cs](../../tests/Full.NET.UnitTests/Document/DocumentOfficePreviewProcessTests.cs) 已覆盖 stdout/stderr 排空和调用取消，本轮仅读取，没有运行或声称这些保护缺失。

明确启用本地进程转换时，源文件大小和进程时限不能约束转换器在时限内写出的 PDF 及中间产物总量。某个转换产生较大输出或大量临时文件时，整个目录会在返回及上传校验之前先占用磁盘；任务独立子目录隔离了文件名，却没有隔离共享临时盘容量。最终上传拒绝超大 PDF、随后删除目录，都无法限制生成期间的磁盘高水位；其他中间文件也不在最终 PdfLength 中。此为代码中的资源预算缺口，尚未执行真实转换器或磁盘写满实验，不声称所有部署都会耗尽磁盘，也不把默认关闭的能力误述为默认运行风险。

该项与 PERF-18 不重复：PERF-18 限定外部 HTTP Provider 的成功响应内存缓冲，本项限定本地子进程在共享磁盘写产物；给 HTTP 响应加读取上限不能阻止本地转换器继续写盘。

后续处理方向：为输出 PDF、单任务临时目录和并发转换的合计磁盘占用建立明确预算，并在写入发生时通过受控进程/文件系统边界落实。仅在进程退出后检查文件长度，或定时查看目录大小，不能作为硬配额证明；中间产物与失败残留也必须纳入。保留现有时限、整个进程树取消、管道排空及任务版本/租约语义，不能为了释放空间提前删除仍在使用的文件或改为无人观察的后台清理。

待验证场景：

- 受控转换器分别生成超预算 PDF、大量中间文件和持续增长文件，验证生成期间触及预算即受控失败，磁盘高水位不突破约定边界；不以最终上传报错代替检查。
- 多 Worker/任务共享临时根目录时，总预算、单任务隔离及其他任务可用空间仍成立；覆盖正常小输出、取消、超时和异常退出后的资源归属与最小失败证据。
- 检查失败不发布可读 PDF、不留下活动子进程或误写成功终态；记录临时磁盘峰值、I/O、任务时长，后续状态持久化及租约验证成对覆盖 SQL Server/MySQL。

## PERF-54：签名正文散列包含多余整段复制

**优先级：P2。第十七轮新增。**

代码位置：

- [SignatureCanonicalRequest.cs](../../src/Modules/Full.NET.Modules.Identity/Security/SignatureCanonicalRequest.cs)，第 133 行 EnableBuffering 让下游可重读正文；第 149–152 行按已知 ContentLength 再创建同容量 MemoryStream，第 155–176 行复制全部正文后 ToArray，再创建完整字节数组；第 125–126 行散列仅需要读取字节内容。
- [SignatureAuthenticationService.cs](../../src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationService.cs)，第 179–184 行生产验签取得完整 body 后立即计算 ContentHash，后续规范字符串只使用散列，不需要保留正文数组。
- [SignatureAuthenticationOptions.cs](../../src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationOptions.cs) 及 [SignatureAuthenticationOptionsValidator.cs](../../src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationOptionsValidator.cs)，默认正文上限 1 MiB，允许配置最大 10 MiB；读取时同时检查声明长度和实际累计字节。本项不是缺少请求体大小限制。
- [SignatureCanonicalRequestTests.cs](../../tests/Full.NET.UnitTests/Identity/SignatureCanonicalRequestTests.cs)，已有正文散列、超限及读取后恢复位置的行为测试；本轮只阅读，未运行或量化分配。

已知长度 L 且读取成功的正文，在框架回放缓冲之外，又分配容量 L 的 MemoryStream 缓冲和长度 L 的 ToArray 结果，并进行两次整段写入/复制。合法 1 MiB 正文仅这两个数组就约有 2 MiB 分配；配置允许的 10 MiB 正文则约有 20 MiB，尚未计入读取块和底层缓冲。并发带正文的签名请求会重复产生这部分分配与复制，但正文数组在生产调用中只用于 SHA-256，可以按字节流增量计算。以上是代码容量推导，不是 GC、峰值进程内存或吞吐实测；框架自身可能使用内存或临时文件回放，不将其也误算为固定的完整内存副本。

本项属于签名认证的输入处理，不是 PERF-28 的 Nonce 过期回收，也不是 PERF-43 的 Provider 响应字节预算；修复那些路径不会移除正文复制。

后续处理方向：在实际读取边界以有界缓冲增量计算正文散列，复用必要的请求回放能力，避免为散列返回完整数组。保持从正文起点计算、成功/失败后恢复原位置、声明和实际长度双重限额、取消释放及逐字节签名协议；不能改变编码、降低密码学算法强度、跳过正文校验，或把未校验的流提前交给业务写入。

待验证场景：

- 空正文、已知/未知长度、分块读取及非零初始位置，与现有 SHA-256/规范字符串结果一致，下游模型绑定仍读取相同正文。
- 默认和最大合法正文、超限、声明长度与实际读取不一致、取消和读取异常均保留既有拒绝及流位置语义；不得因只检查 Content-Length 绕过实际字节预算。
- 在相同正文和并发下记录总分配、大对象分配、GC、复制工作和验签延迟，确认去除额外完整数组；保留 Nonce 防重放、恒时比较、凭据/租户边界，并按受影响入口验证 Native AOT。

## PERF-55：MetricId 修复全量逐行回源

**优先级：P2。第十七轮新增。**

代码位置：

- [TenantQuotaMetricIdReconciliationService.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReconcileQuotaReservationMetricIds/TenantQuotaMetricIdReconciliationService.cs)，第 21–25 行读取全部 MetricId 为空的预留；第 28–37 行每行以 TenantId/MetricCode/默认 PeriodKey 查询指标，即使相同指标已经查过仍重复读取；第 44–54 行实际修复再逐条更新。
- [TenantQuotaSql.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReserveTenantQuota/Persistence/TenantQuotaSql.cs)，第 185–193 行候选按 CreatedAtUtc/Id 排序，但没有页大小、截止范围或续扫游标；第 8–17 行 FindMetric 一次查一个指标，第 195–205 行更新以 MetricId IS NULL 和 Version 为 CAS 条件。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ReconcileQuotaReservationMetricIds/Endpoint.cs)，`/api/v1/tenancy/quota/reservations/reconcile-metric-ids` 有独立精确权限，但请求仅控制 DryRun，没有单次处理预算。
- [TenantQuotaMetricIdReconciliationAssertions.cs](../../tests/Full.NET.IntegrationTests/Api/TenantQuotaMetricIdReconciliationAssertions.cs) 已有授权、预览及修复集成场景，本轮仅读取相关调用信息，未运行。

对 N 条待修复记录，DryRun 需要一次全量读取和 N 次串行指标查询；全部有匹配指标并可更新时，实际修复需要约 1+2N 次业务 SQL。大量预留属于同一租户/指标时，同一 FindMetric 结果也会被反复读取。当前方法不包裹整个集合的显式事务，本项不将其误述为长写事务；风险是单次 HTTP 操作的候选物化、数据库往返和持续时间随历史修复积压增长，缺失指标还会在下一次全量调用继续被扫描。待修复集合为空时成本较小，本项不声称正常配额预留每次都会执行此修复流程。

本项与 PERF-34 不重复：PERF-34 对账的是配额用量基线，需要按租户/指标执行权威 COUNT/SUM；这里修复的是预留记录的物理 MetricId 关联，逐条执行重复的指标定位，两个入口和处理语义不同。

后续处理方向：定义有界候选页及可恢复进度，按本页不同的租户/指标键批量定位，在 Tenancy 自有表内评估有界集合关联/更新，减少重复读取和逐行往返。缺失或竞争失败记录不能永久挡住后续候选；明确每批与整次预览统计语义。保持原先 PeriodKey 匹配规则、MetricId IS NULL、Version CAS、Host 精确权限及跨租户目标配对，不能为减少查询把预留绑到其他租户/指标、误改计量值或跳过并发保护。

待验证场景：

- 双库准备大量同指标及多租户/多指标的空 MetricId 预留，DryRun 和实际修复均只读取有界页；指标查询次数按不同键批次控制，不再每预留一次回源。
- 缺失指标前缀、已有 MetricId、版本竞争、并发补齐及取消/重启后，后续候选仍能处理，已绑定记录和计量余额不被覆盖，预览不产生任何写入。
- 记录扫描/返回行、业务 SQL 次数、处理时长及分配，核对 repaired/skipped 计数和续扫终止条件；不能通过忽略未匹配记录或省略 CAS 伪造性能收益。

## PERF-56：行政区域导入重复执行全表 N+1 快照查询

**优先级：P2。第十八轮新增。**

代码位置：

- [AdministrativeRegionImportService.cs](../../src/Modules/Full.NET.Modules.Regions/Features/ManageAdministrativeRegions/AdministrativeRegionImportService.cs)，第 32 行预览调用 LoadExistingSnapshotsAsync；第 196–231 行先读取全部编码关系，再对每个编码串行 FindByCode 补查完整记录。
- 同一服务第 56–83 行应用先进入事务并调用 PreviewAsync，随后再次调用 LoadExistingSnapshotsAsync，并额外调用 LoadCodeToIdMapAsync 读取全部编码关系；相同数据在一次应用中重复读取。
- [AdministrativeRegionSql.cs](../../src/Modules/Full.NET.Modules.Regions/Persistence/AdministrativeRegionSql.cs)，ListCodeLinks 已在本模块表内 LEFT JOIN 父记录获得 ParentCode，但只投影 Id/Code/ParentCode；FindByCode 再按单个编码读取其余字段。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Regions/Features/ManageAdministrativeRegions/Endpoint.cs)，导入预览和应用均有 RegionsPermissions.Import 精确权限，直接进入上述服务。

假设既有数据稳定为 M 条，一次预览需要约 M+1 次读取；一次应用仅快照与编码映射就需要约 2M+3 次读取，尚未计入逐项写入及清单保存。即使本次 merge 只提交少数区域，也先为整张既有表逐条回源；应用阶段还在写事务内重复付出这段往返、物化和连接驻留。全量差异可能需要覆盖全量源数据，但不需要为每行再单独查询，也不需要在同一次应用中重新构造相同快照。上述为调用次数推导，未执行真实数据库计数或延迟测量。

本项是导入差异读取路径，与此前区域树按需加载优化不同；也不等于 PERF-50 的职位工作簿重复解析，两者分别消耗数据库往返和文件解析工作。PERF-57 的删除集合修复不会消除这里的 N+1。

后续处理方向：用本模块明确的集合投影一次或有界批次读取完整差异字段与父编码，在应用所需的一致性边界内复用同一快照派生预览和 Code→Id 映射。merge 可进一步评估只读必要的目标与父链，但 replace 不能因裁剪而遗漏应比较或移除的区域。不得直接信任事务外旧预览写入，保持环路/父节点校验、稳定编码、原子应用和数据集清单语义。

待验证场景：

- 双库准备大量既有区域，分别预览/应用少量 merge 和完整 replace，快照读取次数不再每区域增长，也不在同一应用中重复加载相同快照。
- 覆盖新增、变更、未变、缺失父级、环路、并发编辑和取消，差异结果与应用清单一致，不能为了减少查询丢掉 ParentCode 或错误复用旧状态。
- 记录 SQL 次数、返回字节、连接/事务时长与锁等待，并保留写入失败后的整体恢复语义；预览保持只读。

## PERF-57：区域 replace 删除集合超过参数预算

**优先级：P1。第十八轮新增。**

代码位置：

- [AdministrativeRegionImportService.cs](../../src/Modules/Full.NET.Modules.Regions/Features/ManageAdministrativeRegions/AdministrativeRegionImportService.cs)，第 154–162 行把 preview.Removed 的全部 Code 物化为数组，单次传给 DeleteByCodes；第 323–362 行没有为整个数据集或待删除集合设置数量预算。
- [AdministrativeRegionImportDiffEngine.cs](../../src/Modules/Full.NET.Modules.Regions/Domain/AdministrativeRegionImportDiffEngine.cs)，replace 将既有集合中未出现在新集合的编码全部加入 Removed，数量不受本次新增/保留项数量约束。
- [AdministrativeRegionSql.cs](../../src/Modules/Full.NET.Modules.Regions/Persistence/AdministrativeRegionSql.cs)，第 234–248 行 SQL Server/MySQL 都使用 `WHERE Code IN @Codes`；[DapperAotEnumerableParameterExpander.cs](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotEnumerableParameterExpander.cs) 按元素展开集合参数，没有为该语句分批。

合法 replace 数据集如果移除超过 2100 个既有编码，会生成超过 SQL Server 上限的删除参数，整个应用无法正常提交，而且此前已经进行了预览、重复快照读取及新增/更新工作。待删除量由旧集合与新集合的差异决定，因此仅限制上传项数也不能解决：提交很小的替换集合仍可能删除大量旧项。MySQL 不适用该相同参数阈值，但一次大删除仍需要明确工作预算。此为参数数量和控制流推导，尚未用真实数据库复现或测量回滚耗时。

本项不同于 PERF-02、PERF-41 的读取集合及 PERF-51 的配置管理请求；它是同一个原子数据集替换中的删除阶段。消除 PERF-56 的 N+1 读取仍会留下这个参数上限错误，反过来仅给删除分批也会保留重复读取。

后续处理方向：采用双库均受控的集合传递/有界删除方式，核算实际参数数与父子删除约束，保持整个 replace 和清单写入在原定原子边界内。不能通过把删除拆成独立提交、只删除前 2100 项、悄悄改为 merge 或吞掉删除失败来完成请求；失败及取消必须有清楚的整体恢复语义。

待验证场景：

- SQL Server/MySQL 均覆盖超过 2100 个待删除编码，包括新集合很小的 replace；检查每条命令参数数、最终区域集合及清单。
- 大量父子区域分布于不同内部批次时仍满足引用和删除顺序，保留区域不被误删；注入中间删除失败/取消，整体应用不留下部分替换。
- 记录大替换的 SQL 次数、事务/回滚时间、日志和锁等待；性能比较不能通过忽略待删除尾部或改变 merge/replace 语义取得。

## PERF-58：聊天详情反复加载并渲染完整历史

**优先级：P2。第十八轮新增。**

代码位置：

- [AiChatSessionQueryService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageChatSessions/AiChatSessionQueryService.cs)，第 70–95 行验证会话归属后，读取全部消息、组装知识答案来源复核集合，再映射完整详情；没有消息页大小或游标。
- [AiChatSql.cs](../../src/Modules/Full.NET.Modules.Ai/Persistence/AiChatSql.cs)，第 174–182 行 ListMessagesBySession 按 SessionId 读取全部消息正文和元数据；与第 184 行起模型输入的最近消息查询是不同 SQL。
- [AiChatContentPolicy.cs](../../src/Modules/Full.NET.Modules.Ai/Domain/AiChatContentPolicy.cs) 及 [AiChatStreamService.cs](../../src/Modules/Full.NET.Modules.Ai/Features/ManageChatSessions/AiChatStreamService.cs)，模型历史限制为 40 条、64000 字符，限制只落实到模型输入读取，并非会话持久历史总量限制。
- [AiChatView.vue](../../ui/admin/src/views/AiChatView.vue)，第 41–44 行 loadSession 加载完整详情；第 132 行发送完成后再次加载；第 139–147 行恢复生成状态最多每 1.5 秒再加载一次、共 20 次；第 277–293 行用 v-for 渲染全部可见消息，没有历史窗口。
- [AiKnowledgeAnswerHistoryGuard.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeAnswerHistoryGuard.cs) 的来源复核有自身 64 条批次预算，但这发生在详情 SQL 已读取所有消息之后，不能作为数据库/HTTP 历史分页证明。

长期使用同一会话会累积多轮持久消息；详情查询、发送完成后的刷新，以及生成状态恢复轮询都会重复读取和传输完整历史，并在浏览器保留和渲染全部正文。即使模型每次只使用最近 40 条，页面历史的数据库字节、序列化、来源元数据处理和 DOM 规模仍随会话累计增长。在历史逐轮增长且每轮完成都重载的情形下，累计历史传输近似呈平方增长；这只是数据量推导，尚未测量具体会话长度下的延迟、浏览器内存或渲染时间。已有单条消息长度及模型上下文限制在此保留，不将模型输入误述为无界。

本项是普通聊天会话详情，不是 PERF-49 的 Agent AG-UI 空轮询，也不是 PERF-27 的站内信刷新并发；优化那些路径不会为聊天消息增加读取窗口。

后续处理方向：会话元数据与有界消息历史分离，提供稳定的历史游标及按需加载；刷新生成状态只取必要状态/增量消息，客户端保留受控展示窗口。知识答案每个历史页仍须完成所需的当前来源授权与失败关闭，页大小要与授权预算协调，不能用分页后的未验证答案或陈旧资格缓存填充页面。保持可信会话所有者/租户边界、消息稳定顺序、取消恢复、引用和反馈语义，模型输入预算继续独立执行。

待验证场景：

- 双库准备远超 40 条的长期会话，首次打开、滚动旧历史、发送完成和生成状态恢复仅读取所需窗口/增量；检查真实 SQL 行、HTTP 字节和 DOM 数量，而非只验证模型输入条数。
- 混合普通消息和超过一个来源复核批次的知识答案，翻页时逐页保留授权、撤权/删除隐藏、引用和反馈能力；不得省略来源复核以压低查询数。
- 同时间戳消息、并发新增、流式完成、取消及断线恢复不重不漏；记录多轮会话的累计传输、分配和渲染时间，保留作用域切换清理及精确权限。

## PERF-59：报表定义及版本列表全量加载完整快照

**优先级：P2。第十九轮新增。**

代码位置：

- [ReportingDefinitionQueryService.cs](../../src/Modules/Full.NET.Modules.Reporting/Features/ManageDefinitions/ReportingDefinitionQueryService.cs)，第 28–51 行定义列表仅接收分组/名称筛选，没有页大小或游标；第 54–73 行历史版本同样一次返回全部结果。第 15 行和第 76 行起已有独立定义详情、单版本读取能力。
- [ReportingDefinitionSql.cs](../../src/Modules/Full.NET.Modules.Reporting/Persistence/ReportingDefinitionSql.cs)，第 8–35 行列表复用完整定义/版本投影，包含 ParameterSchemaJson 与 LayoutConfigJson；第 70–87 行双库定义列表及第 148–156 行版本列表均只有筛选和排序，没有结果窗口。
- [ReportingDefinitionMapper.cs](../../src/Modules/Full.NET.Modules.Reporting/Features/ManageDefinitions/ReportingDefinitionMapper.cs)，第 10–38 行对每条定义或历史版本反序列化参数 Schema，并将完整布局 JSON 带入响应。
- [ReportingDefinitionsView.vue](../../ui/admin/src/views/ReportingDefinitionsView.vue)，第 160–168 行初次加载全部定义，第 388 行发布完成后重新加载所属组全部定义；第 402–408 行打开版本抽屉加载全部快照，而第 630–637 行抽屉只展示版本号、查询 Port、发布时间及说明。
- [ReportingExecuteView.vue](../../ui/admin/src/views/ReportingExecuteView.vue)，第 62–67 行先下载全部定义，再在浏览器筛选已启用且已发布项；执行表单使用所选定义的参数 Schema，不使用所有定义的布局正文。

随着报表定义数和单报表发布次数增长，管理列表、执行页及版本抽屉的数据库读取、参数 Schema 反序列化、HTTP 传输和浏览器驻留均随完整快照总量增长。版本抽屉尤其会为仅展示摘要读取每个历史版本的完整布局；定义详情和单版本接口的存在没有使这些列表按需加载。即使每次写入受请求大小限制，累计记录数量仍没有列表窗口，单项输入边界不能限制一次读取的总字节。此处为静态调用链和数据量推导，未测量实际数据规模下的延迟、分配或数据库 IO，也不将报表执行 SQL 已有的结果行数上限误述为缺失。

本项是 Reporting 自身的定义/发布快照查询，与 PERF-14 的 GoView、PERF-31 的打印模板及 PERF-58 的聊天历史没有共享列表实现；修复那些入口不会改变本项的投影、分页或执行页加载行为。

后续处理方向：定义与版本列表提供有界摘要投影及稳定分页，布局正文和完整参数 Schema 由受保护的详情入口按需读取；执行选择器在服务端筛选可执行定义，选定后读取所需参数，发布后按 ID 刷新必要状态。保持 HostOnly 数据边界、精确权限、稳定排序、版本定位及并发版本检查，不通过悄悄截断选项或改用最新草稿替代已发布快照来降低加载量。SQL Server/MySQL 的查询和客户端契约须一并调整。

待验证场景：

- 双库准备大量定义、停用/未发布定义及含较大布局的多版本历史；确认列表和执行选择器只读取所需页及摘要，版本抽屉不读取布局正文或反序列化未展示的参数 Schema。
- 翻页、分组/名称筛选、发布后刷新、选中定义及按版本查看保持完整可达；相同排序字段、并发新增/发布不造成重复遗漏，所选版本参数与执行快照保持一致。
- 覆盖无权限、Host/租户作用域和取消，详情懒加载不能弱化授权或暴露跨作用域记录；记录 SQL 返回行/字节、分配、HTTP 字节及浏览器驻留，并验证成本受窗口约束。

## PERF-60：其他管理页绕过服务端分页逐页下载全量数据

**优先级：P2。第二十轮新增。**

代码位置：

- [ApiKeysView.vue](../../ui/admin/src/views/ApiKeysView.vue)，第 237–254 行 fetchAllApiKeys 先取 100 条，再按 total 串行遍历余下全部页；第 95–118 行在完整结果上筛选并使用 useArtClientPagination。
- [ConfigEntriesView.vue](../../ui/admin/src/views/ConfigEntriesView.vue)，第 290–307 行 fetchAllEntries/load 同样先下载全部配置，第 118–150 行在本地筛选/分页；第 372、461、488、513、542 行的变更路径再次调用 load。
- [DictTypesView.vue](../../ui/admin/src/views/DictTypesView.vue) 与 [TenantDictTypesView.vue](../../ui/admin/src/views/TenantDictTypesView.vue)，第 463–514 行分别全量下载所有类型及选中类型的所有字典项；第 154–214 行本地筛选/分页，类型重载时还会重载当前选中的字典项。
- 其他已核对的独立实现：[RolesView.vue](../../ui/admin/src/views/RolesView.vue) 第 353–364 行、[HostJobsView.vue](../../ui/admin/src/views/HostJobsView.vue) 第 370–391 行、[TenantsView.vue](../../ui/admin/src/views/TenantsView.vue) 第 315–341 行、[TenantPackagesView.vue](../../ui/admin/src/views/TenantPackagesView.vue) 第 228–245 行，也按 total 串行下载所有页后供本地列表使用。RolesView 第 887–917 行的成员编辑候选还会下载全部 Host 用户后才过滤活动用户。
- [HostApiKeyQueryService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostApiKeys/HostApiKeyQueryService.cs) 第 41–51 行及 [HostConfigEntryQueryService.cs](../../src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/HostConfigEntryQueryService.cs) 第 24–43 行，每个分页请求各执行 COUNT 和列表读取；[HostDictItemQueryService.cs](../../src/Modules/Full.NET.Modules.Settings/Features/ManageHostDictItems/HostDictItemQueryService.cs) 与 [TenantDictItemQueryService.cs](../../src/Modules/Full.NET.Modules.Settings/Features/ManageTenantDictItems/TenantDictItemQueryService.cs) 第 22–54 行还在每页先读取类型身份。

这些页面虽然只展示一页，但首次打开及写后刷新都等待全量下载完成。若某集合有 N 条记录、K=ceil(N/100) 页，单次加载需要 K 次串行 HTTP 请求；上述 API Key/配置路径有 2K 次显式业务 SQL，字典项路径有 3K 次，尚未计入认证授权和其他加载。服务端每页 100 条的边界只约束单次请求，不能约束这一整条页面加载链；浏览器保留、筛选的数据也随总量增长。每次增删改再全量刷新会重复承担这些成本。以上为调用次数和数据规模推导，未实测请求延迟、执行计划、浏览器内存或交互时长。

PERF-03 只登记 UsersView 的独立全量用户加载，PERF-20 是访问日志游标耗尽，PERF-51 是配置批量写入事务；修复这些位置不会改变本项列出的 fetchAll 和页面数据流。本项合并登记其他入口的同类加载设计，不按每个页面重复计数；机构树的完整路径需求不在本项中被简单等同为平铺列表分页。

后续处理方向：普通列表以当前筛选、页码/游标及排序请求服务端受控结果，写后只刷新必要页或记录；用户候选采用有界搜索/分页，并独立保存已选择项。必要的筛选能力要同步补齐 Endpoint、双库 SQL 与客户端契约，不能用只显示前 100 条、提高页大小或无界并行下载替代分页。保持租户切换后的取消/结果隔离、精确读取与操作权限、配置秘密脱敏和保留键排除；角色成员整体替换与跨页选择不能因未加载记录而丢失既有成员。

待验证场景：

- 对上述各类集合准备远超 100 条的数据，首次打开、搜索、翻页和写后刷新仅加载所需窗口；检查 HTTP/SQL 次数、传输字节、首个可用页时间及浏览器驻留量。
- Host/Tenant 字典保持类型归属校验、当前筛选、稳定排序和分页总数语义；修改/删除末页最后一条、切换类型、快速改变筛选与租户时不显示旧结果。
- 角色成员候选跨页选择、取消选择和保存不误删未加载的既有成员；API Key 撤销/状态变化、配置秘密及保留项继续由服务端权威判断，不能用陈旧全量快照作授权依据。

## PERF-61：我的租户邀请逐条回源租户目录

**优先级：P2。第二十轮新增。**

代码位置：

- [MyTenantInvitationQueryService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageMyTenantInvitations/MyTenantInvitationQueryService.cs)，第 23–37 行读取用户邮箱与全部待处理邀请，第 40–47 行对每条邀请串行调用 ResolveActiveByIdAsync；没有按 TenantId 合并读取或列表窗口。
- [TenantResolver.cs](../../src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs)，第 29–47 行的活动解析每次执行 FindById 权威 SQL，不使用 L1/L2。这个安全边界本身是有意设计，不应为了消除本项 N+1 改成陈旧缓存。
- [TenantMembershipSql.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageTenantMembers/Persistence/TenantMembershipSql.cs)，第 246–265 行按当前用户/邮箱、Pending 状态和有效期筛选，按 CreatedAtUtc/Id 排序，但没有页大小或游标。
- [TenantMembershipManagementService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageTenantMembers/TenantMembershipManagementService.cs)，第 89–130 行创建邀请时生成新 Id/TokenHash 并插入，有效期限制为 1～168 小时；[MySQL 223 迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/223_TenancyTenantMembership.sql) 第 46–62 行和 [SQL Server 223 迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/223_TenancyTenantMembership.sql) 第 315–321 行的 TenantId/TargetEmail 索引不唯一，唯一约束是 TokenHash，不能保证同一租户只返回一条当前邀请。
- [TenantContextView.vue](../../ui/admin/src/views/TenantContextView.vue)，第 94–103 行加载完整邀请，第 115、136 行接受后重新加载，第 192–194 行打开页面时加载；[Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageMyTenantInvitations/Endpoint.cs) 从可信主体读取用户 Id 后开放该自助查询。

有 N 条有效待处理邀请时，服务业务链显式执行 2+N 次 SQL，其中 N 次租户解析串行等待；同一租户的多条邀请也会反复查询同一租户。即便有效期只有最多七天，短期内累积的多租户或重复邀请仍会放大请求往返和结果物化；不能将其表述为无限历史全部返回，因为过期/非 Pending 记录已经被 SQL 排除。此为静态调用次数风险，未执行真实数据库复现、邀请规模基准或请求压测。

本项不同于 PERF-09 的知识索引权限检查、PERF-40 的知识库成员校验及 PERF-55 的 MetricId 修复：这里需要跨模块取得活动租户名称/上下文，当前单项解析契约使邀请列表逐条回源，修复其他调用点不会改变该路径。

后续处理方向：邀请列表使用有界稳定窗口，并通过 Tenancy 拥有的最小批量只读 Port 对当前窗口中去重的租户 Id 做权威查询，保持失败传播和活动状态过滤。不得在 Identity SQL 中 JOIN Tenancy 表、放宽用户/邮箱匹配条件，或开启安全数据陈旧缓存；批量集合要有参数预算，不能从 N+1 变成无界 IN。列表过滤后的游标推进需避免停用租户前缀导致空页重复扫描，接受邀请仍须独立执行当前租户和邀请状态复验。

待验证场景：

- 双库覆盖大量有效邀请、同租户重复邀请、跨租户邀请以及停用/不存在租户；显式租户读取按有界批次执行，保持名称、顺序及完整可达性。
- 覆盖 TargetUserId 绑定、邮箱匹配、过期、撤销、已接受及其他用户邀请，不因批量查询或分页暴露不可见记录；租户停用、读取失败及取消保持权威失败语义。
- 接受邀请后刷新、并发创建/接受、同时间戳排序及连续停用租户前缀不重不漏；记录 SQL 次数、窗口行数、HTTP 字节与延迟，容量结论以实际测试规模为限。

## PERF-62：工作流运行时办理人超限判断晚于全量读取和平方级去重

**优先级：P2。第二十一轮新增。**

代码位置：

- [WorkflowAssigneeResolver.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowAssigneeResolver.cs)，第 20 行规定 MaximumResolvedUsers=20；第 36–65 行逐来源将所有结果放进 List，通过 Contains 逐个去重，直到第 67 行才拒绝超限。第 120–132 行角色来源先读取全部活动成员，再 SelectMany/Distinct/ToArray，没有把剩余办理人预算传入目录。
- [WorkflowRoleMemberDirectory.cs](../../src/Modules/Full.NET.Modules.Identity/Workflow/WorkflowRoleMemberDirectory.cs)，第 131–160 行完整读取匹配成员并分组、去重和生成数组；[WorkflowRoleMemberSql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/WorkflowRoleMemberSql.cs) 第 120–170 行 Host/Tenant 语句读取所有匹配活动成员并排序，没有用于运行时溢出判断的有界结果。
- [WorkflowAssigneePolicy.cs](../../src/Modules/Full.NET.Modules.Workflow/Domain/WorkflowAssigneePolicy.cs)，第 40–42 行有最多 8 条来源、每来源 5 个角色及显式用户 20 人的限制；这些配置数量限制没有约束角色展开时的候选人数。[HostRoleMembersService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleMembersService.cs) 第 103–107 行允许一次成员替换包含最多 10000 个用户。
- [WorkflowAssigneePreviewService.cs](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageDefinitions/WorkflowAssigneePreviewService.cs)，第 44–50 行设计器预览直接使用该解析器。
- [WorkflowInstanceManagementService.cs](../../src/Modules/Full.NET.Modules.Workflow/Features/ManageInstances/WorkflowInstanceManagementService.cs)，第 126–136 行开启本模块事务并插入实例，第 185–194 行才解析第一个审批节点的办理人；因此这条启动路径还会在事务已持有数据库资源时承担上述读取和应用层处理，超限后再返回失败并回滚。

当角色中有大量不同活动用户时，解析结果在第 21 个不同用户出现后已经必然超限，但当前仍处理余下成员及其他来源。对单来源 M 个不同用户，List.Contains 的线性查找累计覆盖约 M(M-1)/2 个候选位置，形成 O(M²) 工作；例如角色有 10000 名不同活动成员时，这个规模约为 49995000，而业务最终仍只返回超限错误。该数字是算法规模推导，不代表实际 Guid.Equals 调用数、CPU 指令数或已测耗时。除此之外，SQL 返回、目录分组和多次数组物化仍按全部成员量发生；已有的 20 人业务限制未成为执行成本的提前边界。

本项与 PERF-33 共用角色成员目录，但缺陷并不相同：PERF-33 针对发布前只需要存在性事实的读取；本项针对真正的运行时解析，在已有 20 人上限下仍全量展开并平方级合并。即使发布路径改为存在性投影，设计器预览和启动事务中的这段循环仍然存在。未运行真实数据库复现、应用 CPU 基准或负载压测，不能据此量化端到端收益。

后续处理方向：将 20 人上限作为解析工作预算，使用集合成员判断并在不同用户数确定超限时立即失败；Identity 所有的最小运行时目录 Port 提供有界成员解析及明确溢出信息，避免仅在全量数组返回后优化 Contains。必须按去重后的用户并集判断，保留原有来源有效性、角色活动/超级管理员排除、Host/Tenant 成员资格、默认可信发起人例外和当前权威校验；不能截取前 20 人继续启动，也不能把每个角色的局部截断误当作完整结果。发布存在性与运行时完整结果/溢出契约分别设计，不允许 Workflow 直接 JOIN Identity 表或使用陈旧授权缓存。

待验证场景：

- 双库覆盖 0、1、20、21 及 10000 名不同活动成员的角色，并比较预览、流程启动及审批推进；超限时传回候选量和应用层工作受预算约束，结果仍为原稳定业务错误。
- 多来源/多角色大量重叠、并集恰好 20 或 21 人、显式用户与角色成员重叠时不误拒绝或漏判；缺失/停用角色、空成员、停用用户、跨租户关系和默认发起人语义保持一致。
- 注入成员变化、目录失败及取消，启动事务失败后不残留实例/步骤/待办；记录读取行数、分配、CPU 和事务持有时间，用实际证据区分线性集合合并与读取窗口各自的效果。

## PERF-63：PDF 索引逐批重建完整清单证明

**优先级：P2。第二十二轮新增。**

代码位置：

- [AiKnowledgeIndexPdfVectorCodec.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexPdfVectorCodec.cs)，第 67–79 行每次 ReadBatch 都先调用 ManifestCodec.Decode 完整清单，再取一个 ordinal 校验当前向量批次。
- [AiKnowledgeIndexPdfManifestCodec.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexPdfManifestCodec.cs)，第 34–67 行每次 Decode 复制完整字节、分配全部批次元数据并逐条校验，最后调用 ComputePlanHash；[AiKnowledgeIndexPdfPlan.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexPdfPlan.cs) 第 92–98 行该摘要再次遍历全部批次。
- [AiKnowledgeIndexPdfVectorStore.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexPdfVectorStore.cs)，第 27–49 行 ReadWholePlanProofAsync 遍历所有批次；第 131–145 行每批的 ReadAsync 先调用 CurrentAsync 验证清单，再调用上述 ReadBatch，之后还要复验权威状态。第 171–190 行 CurrentAsync 虽已解码完整清单，但返回 State 时只保留原始行，后续不能复用已验证的清单对象。
- [AiKnowledgeIndexPdfActivationStore.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexPdfActivationStore.cs)，第 120–136 行在事务内完成激活，第 164 行已解码清单；第 178–190 行仍在逐批读取向量时把相同 manifestRow.Payload 传给 ReadBatch，再次完整解码。第 105–109 行激活有 25 秒预算，不能因此描述为无限执行。
- 现有不同实现：[AiKnowledgeIndexDocxVectorCodec.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexDocxVectorCodec.cs) 第 65–80 行支持传入已验证清单并继续验证每批真实载荷；[AiKnowledgeIndexDocxActivationStore.cs](../../src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexDocxActivationStore.cs) 第 200、225 行已在同一轮复用该结果。

对含 B 个批次的合法 PDF 计划，一轮完整向量复核需要 B 次 ReadBatch；每次重新解析、校验和散列 B 条清单元数据，使仅这部分工作达到 O(B²)，同时重复复制同一份清单和分配元数据数组。事务内即使已经在循环前验证完整清单，循环仍重复执行这一工作；后续完成态复核还可能再次走完整验证。清单已有 128 KiB、批次/分块已有 2048 的硬边界，此处不是无界输入缺陷，而是预算内重复工作可能消耗激活时限并延长事务驻留。以上为静态复杂度推导，未测量具体批次数下的 CPU、分配、事务时间或超时率。

本项不同于 PERF-09 的逐权限会话/授权回源；不要求减少原会话、来源、模型目的地、用量账本或真实向量字节的权威复核，也不将逐批处理以控制整代次向量内存的设计本身判为缺陷。即使修复授权回源，PDF 单批 Codec 对相同清单的重复解析仍会保留。

后续处理方向：在一次明确的冻结字节/权威复核边界内生成不可变、已验证的 PDF 清单证明，逐批校验复用该完整计划及指定批次元数据；当前清单原字节、摘要或权威绑定变化时重新验证。不得跨请求缓存授权资格、仅比较表中声明的 Hash、允许部分清单通过，或跳过每批真实载荷 SHA、逻辑坐标和请求指纹；保持事务内最终复核、租约、取消、账本和活动指针原子提交。逐批向量内存预算继续保留，不为节省解析而一次物化整代次向量。

待验证场景：

- 以递增批次数和多页 PDF 覆盖单批恢复、完整代次证明、首次激活及完成态幂等恢复，记录完整清单解析/摘要次数、分配和 CPU；同一冻结清单的解析次数不再逐批增长。
- 篡改完整清单中的非当前批次、顺序、页范围、源摘要或计划摘要，以及当前向量载荷、请求指纹和账本，必须继续拒绝；调用方修改原始数组不能改变已经捕获的证明。
- 双库覆盖模型目的地/会话/来源变化、租约到期、取消及中途失败，事务不留下孤立完成态或部分指针；比较大计划的事务时间与内存峰值，保留既有载荷、分块和时间预算。

## PERF-64：匿名 Office 预览轮询消耗同一口令访问额度

**优先级：P2。第二十二轮新增。**

代码位置：

- [DocumentPublicShareView.vue](../../ui/admin/src/views/DocumentPublicShareView.vue)，第 71–86 行创建预览任务后立即读取状态，未完成则每 1.5 秒再读，最多 40 次；未单独处理 429/Retry-After。第 155–164 行先调用分享 access 再进入内容加载；第 103–125 行把状态请求异常直接转为内容错误。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/Endpoint.cs)，第 167、197、229、254、281 行的 access、原始内容、创建任务、任务状态及预览内容请求都使用同一个 AnonymousShareAccessRateLimitPolicy。
- [DocumentRateLimiterPolicyConfigurator.cs](../../src/Modules/Full.NET.Modules.Document/RateLimiting/DocumentRateLimiterPolicyConfigurator.cs)，第 25–35 行按 RemoteIpAddress 分区、固定一分钟窗口、无等待队列；[DocumentOptions.cs](../../src/Modules/Full.NET.Modules.Document/Configuration/DocumentOptions.cs) 第 13 行及 [Host.Api appsettings.json](../../src/Hosts/Full.NET.Host.Api/appsettings.json) 第 35 行都为每分钟 30 次。
- [HostDocumentShareManagementService.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/HostDocumentShareManagementService.cs)，第 318–337 行每次读取状态都重新 ResolveAnonymousShareAsync；第 386–413 行读取分享后重新验证口令和有效性。[DocumentSharePasswordHasher.cs](../../src/Modules/Full.NET.Modules.Document/Security/DocumentSharePasswordHasher.cs) 使用 Identity PasswordHasher，状态轮询因此重复承担 PBKDF2 验证成本。
- [DocumentOfficePreviewConversionOptions.cs](../../src/Modules/Full.NET.Modules.Document/Configuration/DocumentOfficePreviewConversionOptions.cs)，第 22–28 行转换默认超时 120 秒、Worker 轮询 15 秒；页面约一分钟的状态尝试预算没有覆盖这个服务端处理窗口及排队。

在请求延迟较低、一个限流窗口内的情形下，仅 access 和创建任务就已消耗至少 2 个名额；随后 40 次状态轮询超过剩余 28 个额度，预览内容读取还需要额外名额。慢转换持续约四十多秒时，单个正常浏览器就可能在同一窗口触发 429，并立即结束预览等待，即使后台任务随后仍在正常转换。同一 NAT/IP 下多个标签或用户共享额度，可能更早触发；有口令的分享还会为每次获准轮询重复执行数据库读取和 PBKDF2。即使窗口切换避免了 429，固定 40 次尝试仍可能先于合法的 120 秒转换完成。这里是请求预算和时序推导，尚未进行真实栈慢转换或浏览器复现，不声称所有转换都会失败。

本项不同于 PERF-18、PERF-53 的转换输出预算，也不是 PERF-49 的 Agent 空轮询数据投影；增加转换器字节限制或优化 Agent 查询不会协调匿名预览的状态轮询、口令验证和限流额度。现有限流是必要的防暴力尝试边界，不能把它描述为不存在。

后续处理方向：协同设计有界状态等待与匿名凭据预算，减少重复查询/哈希，按服务端可接受的截止时间、退避及 Retry-After 继续等待或明确提示重试；可评估受控长轮询，但等待前后仍须复验当前分享、口令版本及任务归属。保留防暴力验证、分享撤销/过期、访问次数原子消费和内容下载校验；不能通过全面提高限流、取消口令复核或将分享码视为永久授权解决，也不能无限轮询或反复创建转换任务。

待验证场景：

- 在真实端点与默认每分钟 30 次配置下，覆盖立即完成、40～60 秒完成、接近 120 秒完成和排队；正常预览不会因自身固定轮询耗尽预算而直接失败，记录状态请求数、SQL/哈希次数与可用时间。
- 同一 IP 多标签/多分享、错误口令、限流及 Retry-After、用户取消/页面离开保持有界等待；已创建任务应继续跟踪，不因一次 429 重建或无限重试。
- 等待期间撤销分享、修改口令、过期、文档删除及替换为无关 taskId 必须失败关闭；预览状态读取和恢复不重复消费访问名额，也不绕过最终内容校验。

## PERF-65：机构普通更新执行租户全量父关系读取

**优先级：P2。第二十三轮新增。**

代码位置：

- [TenantUnitManagementService.cs](../../src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/TenantUnitManagementService.cs)，第 37–43 行 UpdateAsync 使用本模块事务；第 137–147 行已读取当前机构及其 ParentId，但第 153–171 行只要请求上级非空就再次检查父级并调用 WouldCreateParentCycleAsync，没有区分上级实际变化与仅更新名称/排序。
- 同文件第 285–318 行 WouldCreateParentCycleAsync 每次先读取全部父关系、ToDictionary，再沿拟议上级逐个向上遍历；已有 seen 集合可截断脏环，不是无环保护缺失。
- [OrganizationSql.cs](../../src/Modules/Full.NET.Modules.Organization/Persistence/OrganizationSql.cs)，第 86–94 行 ListUnitParentLinks 只按可信 TenantId 筛选，返回该租户全部机构 Id/ParentId，没有目标父链范围。
- [OrgUnitsView.vue](../../ui/admin/src/views/OrgUnitsView.vue)，第 323 行编辑时保留 unit.parentId，第 385–390 行普通更新将该父级原样提交；不改变上级的名称修改也能触发这条读取链。

设当前租户有 T 个机构、目标机构的祖先链只有 d 层：每次更新非根机构仍会返回 T 条关系并分配整张字典，再完成只需要沿 d 层关系进行的环路检查。即使本次完全不改变 ParentId，读取成本也随全租户机构数增长，并发生在更新事务内部。大量互不相关机构只会增加此次更新的传输、映射和事务持有成本；不能将其描述为跨租户读取，因为 SQL 已绑定当前可信租户。上述为结果规模与调用链推导，未测量双库执行计划、实际锁等待或延迟。

本项不同于 PERF-02 的机构参考用户 ID 展开、PERF-56 的 Regions 导入 N+1，以及 PERF-60 的普通管理页全量分页：这里是 Organization 单条写入前的层级检查，修复那些读取页面或导入入口不会减少这次租户父关系物化。

后续处理方向：在当前权威记录和并发控制边界内区分父关系变更；确需检查时只读取必要祖先链，并明确深度、取消及异常环路的处理预算，双库采用一致语义。不能简单关闭父级校验、信任客户端树或为了少读而忽略已有脏环；父级存在/活动状态、可信租户、版本冲突、并发调整关系、事务与 Outbox 原子性仍须保留。若要跳过未变父级的检查，应明确并验证原有损坏关系的失败关闭要求，不能顺带放宽该行为。

待验证场景：

- 双库准备相同短父链、不同规模的其他机构；更新名称/排序及真正调整上级时，返回关系量与所需检查范围相称，记录 SQL 行/字节、分配与事务时长。
- 覆盖根/非根、父级未变、改挂自身/后代、父级不存在或停用、已有脏环、超深链和跨租户标识，保持原稳定结果或明确批准的新预算错误。
- 并发改挂、版本过期、取消及 Outbox 写入失败不产生环、部分更新或缺失事件；不能为性能省掉当前状态复验或拆开原子提交。

## PERF-66：Realtime 探针取消后丢失迟到 Redis 连接所有权

**优先级：P2。第二十三轮新增。**

代码位置：

- [RealtimeBackplaneProbe.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneProbe.cs)，第 30–38 行启动不接收该取消令牌的 ConnectionMultiplexer.ConnectAsync，再对返回任务调用 WaitAsync；await using 只有在等待成功返回连接后才接管资源，没有在取消分支保留原始任务或释放迟到连接。
- 同文件第 39–42 行的 PingAsync 位于连接已被 await using 接管之后；该阶段取消可进入释放，与连接创建阶段的缺口不同。ConnectTimeout/AsyncTimeout 已设为 1000ms，不能将其描述为无网络超时。
- [RealtimeBackplaneHealthCheck.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/Health/RealtimeBackplaneHealthCheck.cs)，第 30–36 行合并调用方取消并设置两秒本地预算，随后进入探针；调用方在连接完成前取消就可能先结束 WaitAsync。
- [ServiceCollectionExtensions.cs](../../src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs)，第 137–153 行配置 Redis Backplane 时注册该真实探针及 ready 检查；[HealthEndpointExtensions.cs](../../src/BuildingBlocks/Full.NET.Hosting/Observability/HealthEndpointExtensions.cs) 将带 ready 标签的检查映射到 /health/ready。
- [RealtimeBackplaneTelemetryTests.cs](../../tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneTelemetryTests.cs)，第 104–115 行调用方取消场景使用 StubRealtimeBackplaneProbe，仅验证健康检查传播取消，未覆盖真实连接任务取消等待后的资源交接。

触发顺序是：连接任务已启动，取消先被 WaitAsync 观察，之后底层连接任务成功返回 Multiplexer。取消等待不会取消这个已启动的连接任务；由于 await using 的初始化没有成功完成，迟到连接没有进入当前作用域的释放路径，也没有其他显式收尾所有者。反复取消探测可能使 Redis 连接及其后台资源滞留，给本应轻量的健康检查引入额外资源压力。此处只登记该明确时序下的生命周期风险，不声称正常完成的每次探测都泄漏，也未实测真实 SDK/Redis 下的滞留时间、连接数或内存增长。

Realtime 特意用独立短连接验证当前可达性，不复用 SignalR 运行连接；本项不以改变这项探测语义作为优化前提。[DistributedCacheHealthCheck.cs](../../src/BuildingBlocks/Full.NET.Caching.Fusion/Health/DistributedCacheHealthCheck.cs) 使用受管理 L2，取消时观察原任务，其资源所有权场景与本项不同。PERF-44 的 OCR/K3Cloud Handler 复用也不能补上这个取消后的连接交接。

后续处理方向：连接任务从启动到完成/失败始终有明确所有者，取消早返回时也应观察后续结果并释放迟到成功的连接；预先取消的请求应在创建连接前结束。收尾须有可观察、受控的生命周期与并发预算，不能用无人观察的 fire-and-forget 清理或无界待清理任务替代。保持探针独立可达性判定、调用方取消传播、两次失败滞回、稳定遥测和敏感连接信息不外泄；连接获取后的 Ping 失败/取消仍须正常释放。

待验证场景：

- 用可控制完成顺序的连接创建替身覆盖先取消后连接成功、连接失败、预先取消、正常成功及 Ping 阶段取消；断言每个成功创建的连接最终恰好释放一次，调用方取消仍及时传播。
- 真实 Redis 重复执行早取消、慢握手和恢复，记录客户端连接数、任务/句柄及内存随轮次的变化，确认无持续滞留；同时覆盖多探针并发和宿主关闭收尾。
- 保留正常 ready 成功、连续失败降级/摘流、取消不记为虚假故障，以及无 Redis 配置时的原行为；资源修复不能依赖忽略故障或复用陈旧健康结果。

## PERF-67：Jobs 健康状态全量加载不断累积的 Worker 实例

**优先级：P2。第二十四轮新增。**

代码位置：

- [JobWorkerHeartbeatService.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobWorkerHeartbeatService.cs)，第 18–19 行在服务构造时生成新的 UUID v7 实例标识和启动时间，第 21–49 行只按该标识 Upsert 心跳；[JobsModule.cs](../../src/Modules/Full.NET.Modules.Jobs/JobsModule.cs) 第 112 行将其注册为 Singleton，因此同一进程的各轮心跳复用同一行，进程重启后使用新标识。
- [JobSql.cs](../../src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs)，第 990–1022 行双库 Upsert 仅插入或更新当前实例；第 1024–1034 行 ListWorkerInstances 读取全部 Host 实例，只有心跳倒序排序，没有保留时间、页大小或游标。当前模块、脚本及部署路径未见针对该表的过期回收实现。
- [HostJobHealthQueryService.cs](../../src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobHealth/HostJobHealthQueryService.cs)，第 25–47 行每次查询物化全部实例，在应用层为每行判断 stale 并完整映射到响应；stale 标记不会减少读取或存储数量。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobHealth/Endpoint.cs)，第 16–33 行 Host 健康接口没有实例分页参数，受 jobs.health.read 精确权限保护。
- [HostJobHealthView.vue](../../ui/admin/src/views/HostJobHealthView.vue)，第 36 行初次加载完整健康响应，第 87–100 行直接将 health.workers 交给普通表格显示，没有历史窗口或分页。

多实例长期运行、进程重启、滚动发布和扩缩容会使旧 InstanceId 留在心跳表；停掉的进程不再更新，但其记录继续参与每次健康列表读取和响应。存量规模随累计启动次数而不是当前存活 Worker 数增长，数据库结果排序/传输、应用映射、HTTP 字节和浏览器表格规模因此持续扩大。健康查询已有权限及 Host 数据边界，本项不涉及绕过隔离；同一存活进程也不会每轮新增一行。以上为持久化生命周期和查询规模推导，未测量长期部署数据量下的数据库 IO、页面延迟或内存。

本项是 Jobs Worker 观测元数据的保留与查询边界，不是 PERF-22 的 AI Worker 心跳被长任务阻塞，也不是 PERF-23 的计划派发等待整批执行；调整心跳发送时机或派发调度仍会留下这些历史实例及全量读取。

后续处理方向：明确活动实例、近期失联实例与历史诊断记录的保留边界，健康响应使用有界的当前/近期投影，必要历史提供稳定分页；过期元数据使用双库成对的有界清理。保留足够失联诊断信息和真实 stale 语义，不能只返回最新一条而丢失仍存活的其他实例，也不能按 HostProfile 合并不同进程掩盖并发 Worker。心跳更新与清理竞争应以权威心跳时间条件处理，不能影响运行任务、租约、重试、幂等或事件可靠性。

待验证场景：

- 双库准备少量活动实例、大量近期/长期失联实例及多次重启记录；健康响应和页面规模受约定窗口约束，必要历史仍可分页定位，记录返回行/字节、SQL 时长和浏览器驻留。
- 同一主机多个进程、滚动发布新旧并存、心跳延迟及恢复不误合并、不误隐藏活动实例；保留原有 stale 判定或显式批准后的观测契约。
- 清理中并发更新心跳、取消及批次失败不删除已恢复实例或造成无界清理事务；执行任务的租约和终态写入不依赖被回收的观测历史，结果及失败恢复保持正确。

## PERF-68：租户订阅简单判断和单项操作读取全部历史

**优先级：P2。第二十五轮新增。**

代码位置：

- [TenantSubscriptionManagementService.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ManageTenantSubscriptions/TenantSubscriptionManagementService.cs)，第 28–43 行创建/取消均进入本模块事务；第 78–83 行创建时读取 ListByTenant 全部结果，再 Any 判断 Trial/Active/PastDue 是否存在；第 141–146 行取消时同样先全量读取，再用 subscriptionId 找一个目标。
- [TenantCommercialReactivateGate.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ManageTenantLifecycle/TenantCommercialReactivateGate.cs)，第 53–58 行在商业约束生效且未绑定套餐的分支，读取全部订阅后仅判断活动状态是否存在。
- [TenantSubscriptionSql.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ManageTenantSubscriptions/Persistence/TenantSubscriptionSql.cs)，第 8–17 行 ListByTenant 按 TenantId 返回所有状态的完整订阅记录并排序，没有状态、Id 或结果窗口。
- [MySQL 226 迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/226_TenancyTenantSubscription.sql) 第 14–19 行使用 ActiveSubscriptionKey 约束活动订阅唯一；[SQL Server 226 迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/226_TenancyTenantSubscription.sql) 第 129 行起使用活动状态筛选唯一索引。两者允许同租户保留多条已取消订阅，活动唯一不代表历史总量有界。
- [TenantSubscriptionQueryService.cs](../../src/Modules/Full.NET.Modules.Tenancy/Features/ManageTenantSubscriptions/TenantSubscriptionQueryService.cs)，第 11–21 行列表也复用完整历史读取；[TenantsView.vue](../../ui/admin/src/views/TenantsView.vue) 第 466–475 行打开订阅弹窗全量加载，第 497、515 行创建/取消完成后再次加载全部订阅。

设某租户累计保留 N 条订阅历史，判断活动订阅是否存在、取消一个指定订阅都读取并物化 N 条记录，即使活动唯一索引已保证活动集合最多一条。生命周期检查也承担相同成本，订阅弹窗将全部历史传至浏览器。随着反复创建/取消及历史保留，操作成本随历史总量增长，而所需事实只是一项存在性或一个确定 Id；管理写入路径还在事务中承担这些多余读取、排序结果传输和对象分配。这里只登记静态查询规模风险，未测量双库执行计划、锁等待、延迟或实际订阅规模。

本项与 PERF-34 的配额全租户聚合、PERF-55 的 MetricId 修复没有共用查询；PERF-60 虽包含 TenantsView 主列表的 fetchAllTenants，但未覆盖订阅弹窗、写前判断及商业门禁。只修复租户主列表分页不会改变本项 ListByTenant 的用途混合。

后续处理方向：为活动状态存在性、指定 TenantId/Id 读取与有界历史列表分别提供最小 SQL 投影；双库保持相同状态判断、排序和分页语义，取消继续通过版本条件更新。保持 Host 管理权限、目标租户绑定、活动订阅唯一约束及套餐绑定/履约的事务边界，不以取历史第一页代替存在性判断，也不为减少读取擅自删除业务历史、按时间改写活动状态定义或拆开原子提交。

待验证场景：

- 双库准备大量已取消历史及零/一条活动订阅，创建判断、商业门禁和指定订阅取消不再返回完整历史；记录返回行/字节、SQL 执行和事务时长。
- Trial、Active、PastDue、Cancelled 各状态以及历史日期组合保持现行资格结论；并发创建仍由活动唯一约束兜底，跨租户 Id、版本过期和不存在订阅继续拒绝。
- 订阅弹窗稳定翻页、创建/取消后刷新和目标历史定位不重不漏；履约、套餐绑定失败和取消均保持原子恢复，必要业务历史完整保留。

## PERF-69：审计工作簿全表 DOM 物化且生成阶段不可取消

**优先级：P2。第二十六轮新增。**

代码位置：

- [HostAuditLogExportService.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/ExportHostAuditLogs/HostAuditLogExportService.cs)，第 114–180 行三类导出均用 QueryAsync 完整读取当前导出结果，再转换为工作簿行；第 183–202 行 BuildResult 将全部行 ToArray 后同步调用 Codec，不接收或检查 CancellationToken。
- [AuditLogWorkbookCodec.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/ExportHostAuditLogs/AuditLogWorkbookCodec.cs)，第 23–38 行再次将行序列物化；第 113–127 行为全部行/单元格构建 XElement/XDocument，第 148–158 行才通过 XmlWriter 保存完整文档，第 130 行从 MemoryStream 复制完整压缩结果。写入接口使用 XmlWriter 并不代表工作表逐行生成，因为整棵文档树在 Save 之前已构建。
- 同 Codec 的 Export、CreateWorkbook、WriteEntry 均没有取消参数或循环检查，生成工作表、清理 XML 文本、压缩及最终复制在 SQL 返回后继续同步执行。
- [AuditingQueryOptions.cs](../../src/Modules/Full.NET.Modules.Auditing/AuditingQueryOptions.cs)，第 21、52–55 行默认最多导出 5000 行，配置上限 20000；[AuditingExportTimeRangePolicy.cs](../../src/Modules/Full.NET.Modules.Auditing/AuditingExportTimeRangePolicy.cs) 校验显式时间窗、保留区间及导出行数。这些已有边界保留，本项不是无行数上限。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/ExportHostAuditLogs/Endpoint.cs)，第 64–79 行传入请求取消令牌并独立检查敏感字段权限，第 99 行起把完整 FileBytes 作为文件返回；取消令牌没有贯通到 Codec。

在默认上限附近或配置到 20000 行时，单次导出同时持有数据库结果对象、格式化行/文本、完整工作表 DOM 以及压缩输出；较大的操作日志导出可生成约 26 万个数据单元格及其 XML 子节点/属性，多个导出并发时会叠加分配与内存峰值。若客户端在 SQL 已完成后取消或离开页面，工作簿阶段仍会继续构建和压缩本次不再需要的文件。此处为物化与取消调用链的静态风险，未测量实际峰值、GC、生成耗时或取消后的 CPU；不能据此断言已发生 OOM。

当前异常写入链 [ExceptionLogMiddleware.cs](../../src/Modules/Full.NET.Modules.Auditing/Middleware/ExceptionLogMiddleware.cs) 第 78–82 行采用固定安全消息并写入空 StackTrace，异常表本身也有字段长度限制；本项不依赖把原始大异常正文写入审计表。它不同于 PERF-50 的工作簿反复解析、PERF-18/53 的 Office 输出预算，也不同于报表导出实现；[ReportingExcelExportRenderer.cs](../../src/Modules/Full.NET.Modules.Reporting/Domain/ReportingExcelExportRenderer.cs) 已有预算并逐行写工作表，不应一并误报为同样的 DOM 路径。

后续处理方向：工作表使用逐行 XML 写入，减少全部格式化行与 DOM 的同时驻留，并让取消贯通到行转换、写入和压缩边界；按实际日志投影定义输入/输出总字节及并发预算，明确超限结果。若最终仍返回 byte[]，应保留受控总大小并如实披露末端缓冲，不能把它称为端到端零缓冲。保持时间窗/保留期、字段权限、Host 数据边界、公式转义、非法 XML 字符处理及行数截断元数据；不能为了节省内存丢行、放宽敏感字段授权或削弱审计可靠性。

待验证场景：

- 双库按默认 5000 行与配置上限 20000 行导出 Access/Operation/Exception，覆盖较长合法路径、XML 特殊字符和敏感字段开关；记录峰值、分配、GC 和生成时长，校验工作簿内容、行数与截断元数据。
- 在 SQL 完成后、行转换中及写入中取消请求，确认生成及时停止且临时资源释放；压缩和单次同步写入若不可抢占，应明确其剩余工作预算，不承诺未验证的硬截止时间。
- 多个合法导出并发时总资源受控，超限/失败返回稳定结果；公式样式文本、无权限、保留期边界和摘要审计继续正确，不改变 B0/B1/B2 可靠性契约。

## PERF-70：通知及支付客户端缺少响应字节预算

**优先级：P2。第二十七轮新增。**

代码位置：

- [HttpAliyunSmsTransport.cs](../../src/Modules/Full.NET.Modules.Notifications/Providers/AliyunSms/HttpAliyunSmsTransport.cs)，第 39、54–55 行使用默认 SendAsync，再完整读取字符串，之后才检查状态码；错误响应也先读取正文。
- [HttpDingTalkTransport.cs](../../src/Modules/Full.NET.Modules.Notifications/Providers/DingTalk/HttpDingTalkTransport.cs)，第 29、44、74、89 行令牌及发送调用采用相同读取方式；审批同步第 150、165、193、208 行也完整接收响应。
- [HttpWeComTransport.cs](../../src/Modules/Full.NET.Modules.Notifications/Providers/WeCom/HttpWeComTransport.cs)，第 27、42、86、101 行；[HttpWeChatMiniProgramTransport.cs](../../src/Modules/Full.NET.Modules.Notifications/Providers/WeChatMiniProgram/HttpWeChatMiniProgramTransport.cs)，第 74、89、107、122 行，令牌与发送响应均先整体接收、解码再处理。
- [WeChatNativePayClient.cs](../../src/Modules/Full.NET.Modules.Payments/Connectivity/WeChatNativePayClient.cs)，第 84–99 行支付请求默认 SendAsync 后读取完整字符串，再检查状态并反序列化；第 296–298 行共享请求方法同样完整读取。[AlipayPagePayClient.cs](../../src/Modules/Full.NET.Modules.Payments/Connectivity/AlipayPagePayClient.cs)，第 109–110 行查询调用默认 GetAsync 后读取完整字符串。
- [NotificationsModule.cs](../../src/Modules/Full.NET.Modules.Notifications/NotificationsModule.cs)，第 273–274、289–290、317–318、331–332 行为上述通知提供方注册工厂客户端及 30 秒超时；[PaymentsModule.cs](../../src/Modules/Full.NET.Modules.Payments/PaymentsModule.cs)，第 45–54 行支付客户端也有工厂和 30 秒超时。这些客户端未设置与协议相称的 MaxResponseContentBufferSize，调用链也未在正文解码前采用受限读取。

上述默认 GetAsync/SendAsync 使用完整内容完成模式，返回给调用方前已缓冲正文，随后 ReadAsStringAsync 再解码字符串，成功路径还会解析 JSON。当提供方或中间代理返回异常大的 JSON/HTML，尤其无可信 Content-Length 或在超时内快速返回大正文时，会先付出完整缓冲与字符串分配成本；并发通知、令牌获取和支付查询会叠加这一成本。30 秒超时约束持续时间，不能代替适合小型协议响应的字节预算；SDK 自身存在缓冲上限，本项不将它描述为字面上的无限读取。此为静态资源风险，未测量实际响应规模、峰值、GC 或吞吐影响。

本项与 PERF-43 的 OCR/K3Cloud 响应预算属于相同风险模式，但涉及独立命名客户端与传输实现，修复该项不会覆盖这些路径；因此将本轮通知及支付提供方合并登记，不逐个拆项。它也不是 PERF-44 的客户端生命周期问题：这里已使用工厂客户端，不应误述为每次重建连接池。既有 OSS、Office 和 Webhook 的读取边界按各自条目或实现评价，不能据此推断全仓都缺少响应限制。

后续处理方向：按协议定义成功及错误响应的最大接收字节数，在缓冲、解码及 JSON 解析之前生效；可采用合适的命名客户端缓冲上限，或 ResponseHeadersRead 配合受限正文读取。仅在 ReadAsStringAsync 后截断文本或只检查 Content-Length 均不足以形成边界；采用流式完成模式时，应让同一取消/截止预算继续覆盖正文阶段，并保证超限、取消及异常时释放响应和流。保持固定目标、凭证、签名校验、错误分类、租户隔离和租约版本语义。

发送或支付请求发出后，读取超限不能证明外部未受理，必须保留外部结果未知及现行幂等、查询和对账恢复语义；不能以超限为由盲目重发通知/支付，或直接回退订单、退款状态。

待验证场景：

- 各提供方正常成功、业务错误及 HTTP 错误仍按协议处理；覆盖预算边界及超一字节、无 Content-Length、错误长度和分块大正文，确认在解码/解析前限制实际接收字节。
- 覆盖快速大响应、慢正文、取消和连接中断，核对正文阶段截止、资源释放及并发分配峰值；不能仅以请求头到达判定完成。
- 对已经发出的发送/支付操作模拟读取超限及解析失败，确认未知结果、幂等、租约和对账恢复不变，跨租户、失效凭证及签名异常仍被拒绝。

## PERF-71：角色自定义数据范围逐机构校验与写入

**优先级：P2。第二十八轮新增。**

代码位置：

- [HostRoleDataScopeService.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/HostRoleDataScopeService.cs)，第 41–50 行先完成请求校验再开启更新事务；第 64–90 行对 UnitIds 去重，只检查非空，随后逐个 await FindActiveUnitAsync，没有机构数量预算。第 105–118 行在上述校验结束后才读取目标角色并拒绝不存在、系统或超级管理员角色。
- [OrganizationUnitProjectionDirectory.cs](../../src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/OrganizationUnitProjectionDirectory.cs)，第 11–21 行每次 FindActiveUnitAsync 都执行单条查询；[OrganizationUnitProjectionSql.cs](../../src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/OrganizationUnitProjectionSql.cs)，第 8–18 行按显式 TenantId、UnitId 和活动状态读取 Identity 本地投影，没有批量校验端口。
- 同一 HostRoleDataScopeService 第 126–153 行在更新角色版本、删除旧集合后，逐机构 await InsertRoleDataScopeUnit；直到第 154 行重新读取角色与完整集合后才返回事务调用。事务内的串行写入次数随机构数增长。
- [IdentitySql.cs](../../src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs)，第 1989–2031 行对应集合读取、整组删除、单行插入和带版本条件的角色更新；[RoleDataScopeContracts.cs](../../src/Modules/Full.NET.Modules.Identity.Contracts/RoleDataScopeContracts.cs)，第 51–55 行请求接受 IReadOnlyList<Guid>，没有数量约束。[Endpoint.cs](../../src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/Endpoint.cs)，第 265–281 行直接调用上述服务，使用 AssignDataScope 精确权限，没有额外集合数量校验。

对包含 N 个不同且有效机构的 Custom 请求，机构校验产生 N 次串行 SQL，事务内再产生 N 次单行插入，此外还有固定的角色读取、版本更新、旧集合删除和返回读取。即使重复提交同一集合，当前也会重新校验并删除重写全部关系；更新角色后，相关行锁及事务连接要等待全部插入和返回读取才能释放。缺少业务集合预算时，合法大集合会放大往返和事务占用；由于角色资格判断晚于机构校验，不存在或禁止更新的目标也可能先付出 N 次查询成本。本项是静态调用次数与事务范围风险，未测量双库延迟、锁等待或连接池影响，不能据此承诺具体性能收益。

该接口已有权限、显式目标租户、机构活动状态、受保护角色和乐观版本检查；并非匿名入口或无需授权即可更改范围。问题不同于 PERF-37 的文档 ACL、PERF-51 的系统配置批量写入及 PERF-62 的工作流成员展开，涉及独立服务和关系写入，修复这些旧项不会覆盖本路径。运行时 [RoleDataScopeProjection.cs](../../src/Modules/Full.NET.Modules.Identity/DataScope/RoleDataScopeProjection.cs) 使用角色参数和关系子查询，本项不声称 N 个机构会直接生成 N 个 SQL 参数，也不将当前参数化查询误报为注入风险。

后续处理方向：在昂贵校验前定义原始输入及去重后的机构数量预算，尽早拒绝不合法或不可更新目标，并通过 Identity 所有的最小批量投影查询校验目标租户全部机构；关系替换使用双库适配且受参数/批次预算约束的集合写入，评估同集合不重写及差量更新的收益。批量校验必须证明每个不同机构存在且活动，不能只比较含重复输入的数量、截取部分机构后成功，或绕过模块边界直接查询 Organization 表；不得用无界并行单行查询代替批量设计。保留 Host 与精确权限、明确目标租户、受保护角色、版本冲突及整组替换的原子语义，不能提交部分授权范围或以陈旧缓存作放行依据。

待验证场景：

- 双库覆盖 1 个、较大合法集合、预算边界及超限、重复 ID 和重复提交；统计校验/写入 SQL 次数、事务时长、锁等待及连接池占用，证明成本由明确批次预算控制。
- 无权限、不存在角色、系统/超级管理员角色、缺少目标租户、跨租户或停用/不存在机构继续拒绝；畸形及超限输入应在逐项数据库工作前结束，拒绝结果不得残留部分关系。
- 并发替换、旧 Version、执行中取消、批次中途失败保持原子性及版本冲突语义；范围读取与运行时过滤的集合一致，不通过丢弃未处理机构来减小响应或 SQL 工作量。

## PERF-72：Settings 域审计 TraceId 读取缺少匹配访问路径

**优先级：P2。第二十九轮新增。**

代码位置：

- [SettingsDomainAuditReadSql.cs](../../src/Modules/Full.NET.Modules.Settings/Persistence/SettingsDomainAuditReadSql.cs)，第 8–20 行 ListByTraceId 使用 WHERE TraceId = @TraceId 并按 OccurredAtUtc 倒序，读取差异 JSON，没有时间范围或分页；[SettingsDomainAuditChangeDiffReader.cs](../../src/Modules/Full.NET.Modules.Settings/Auditing/SettingsDomainAuditChangeDiffReader.cs)，第 13–25 行完整读取并映射结果。
- [SQL Server 050_SettingsDomainAudit.sql](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/050_SettingsDomainAudit.sql)，第 19 行主键为 Id，第 221–222 行聚集索引为 (OccurredAtUtc, Id)，第 294–295 行另建 (TenantId, OccurredAtUtc, Id) 索引；[MySQL 同名迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/050_SettingsDomainAudit.sql)，第 15、18、71–72 行对应 Id 主键、时间及租户/时间索引。全迁移目录中该表仅由 050 及补充 CorrelationJson 的 265 脚本涉及，未发现以 TraceId 为前导列的索引。
- [DomainAuditChangeDiffQueryService.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/QueryDomainChangeDiffs/DomainAuditChangeDiffQueryService.cs)，第 27–36 行限制 TraceId 长度和字符，第 40–46 行调用各模块差异读取器；[Endpoint.cs](../../src/Modules/Full.NET.Modules.Auditing/Features/QueryDomainChangeDiffs/Endpoint.cs)，第 15–32 行只接收 TraceId 并要求差异读取权限，接口未提供时间筛选。
- [AuditLogDetailDrawer.vue](../../ui/admin/src/views/components/AuditLogDetailDrawer.vue)，第 118–122 行按 TraceId 加载差异；[DiagnosticPolicyAuditWriter.cs](../../src/Modules/Full.NET.Modules.Settings/Persistence/DiagnosticPolicyAuditWriter.cs)，第 24–38 行将诊断策略变更的 TraceId 及差异直接写入 B0 审计表。

按仓库迁移定义的结构，TraceId 等值条件与已有 Id、时间、租户/时间前导键不匹配，且查询没有时间范围来缩小历史候选。随着设置域审计历史增长，差异读取存在检查大量无关历史页的风险；即使某个 TraceId 在 Settings 中没有记录，也不能仅凭返回零行认定查询成本很低。若同一 TraceId 对应多条记录，读取器还会完整返回及解析全部差异。本项不假设每次查询都返回很多行，也不把审计写入频率描述为高频；未读取实际部署索引或执行计划，不能断言数据库已经采用某种扫描或量化 IO/耗时。

这不同于 PERF-20 的访问日志前端全量分页、PERF-45 的清理调度和 PERF-69 的工作簿 DOM，属于独立的 Settings B0 审计追踪读取。其他模块提供时间索引或普通日志 TraceId 索引不会自动覆盖此表；不能把 B0 审计历史按 B2 日志预算采样或删除来规避查询成本。

后续处理方向：以双库代表性历史规模和实际计划评估 TraceId 加稳定排序键的访问路径，权衡索引宽度、回表及 B0 写放大；不要未经测量就把完整差异 JSON 纳入覆盖索引。为多记录追踪定义有界、可继续读取的契约；如引入时间条件，必须来自可靠事件定位或显式查询范围，不能用任意短时间窗静默遗漏同一追踪的合法审计。保留 Host/租户边界、精确差异权限、敏感字段脱敏及旧记录不可解析状态，不跨模块直读，也不改变 B0 同事务可靠性。

待验证场景：

- SQL Server/MySQL 在代表性历史规模下查询最新、较旧、不存在及重复 TraceId，记录实际计划、逻辑读、排序和延迟；同时评估新增访问路径对 B0 插入吞吐、日志量及存储的影响。
- 同时间戳多条差异、多模块结果、缺少/不可解析 JSON 及必要历史读取保持完整、稳定；若分页或截断，必须向客户端明确继续读取信息，不能把前 N 条伪装成全部。
- 无权限、租户上下文及取消继续遵守现行拒绝/隔离语义；索引部署或恢复失败不丢失审计事实，不降低审计写入可靠性。

## PERF-73：单对象元数据查询重复读取完整目录

**优先级：P2。第二十九轮新增。**

代码位置：

- [CodeGenerationCatalogQueryService.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/BrowseHostCatalog/CodeGenerationCatalogQueryService.cs)，第 249–272 行 ResolveTableAsync 为确认一个表存在，调用 ReadObjectNamesAsync 读取全部基础表，再用 ordinal FirstOrDefault 匹配；第 275–311 行 ResolveObjectAsync 先读全表目录，找不到后再读全部视图目录。
- 同文件第 314–341 行 ReadObjectNamesAsync 通过 QueryAsync 物化所有目录名，然后过滤安全名称、Distinct、OrderBy、ToArray，没有将目标名称传入 SQL；第 87、125、166、192 行元数据、迁移草案、列列表及列同步均进入上述解析路径。第 344–370 行列读取本身已带 TableName 条件，不能将其误述为读取全库所有列。
- [DatabaseCatalogQueries.cs](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCatalogQueries.cs)，第 9–42 行表/视图查询只按 dbo 或 DATABASE() 及对象类型过滤，没有目标名称条件；第 45–76 行列查询则已按 TableName 参数过滤。[CodeGenerationCatalogSql.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationCatalogSql.cs)，第 11–39 行将这些语句注册为 HostOnly。
- [CodeGenerationCatalogView.vue](../../ui/admin/src/views/CodeGenerationCatalogView.vue)，第 106、122 行单项元数据与迁移草案操作，[CodeGenerationPreviewsView.vue](../../ui/admin/src/views/CodeGenerationPreviewsView.vue)，第 348 行列同步，都会触发该服务。[Endpoint.cs](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/BrowseHostCatalog/Endpoint.cs) 为这些接口要求目录读取精确权限。

当当前数据库有 T 个基础表及 V 个视图时，即便只查询一个对象，也会把 T 个名称传回应用、去重和排序；视图或不存在对象还要额外完整处理 V 个名称，之后成功路径才查询目标列。单次列同步、读取列或生成草案会重复该工作，成本随整个数据库目录增长，而不是只随目标列数增长。这里的浪费是单项存在性/类型解析复用了全目录读取，不是正常目录浏览必须返回多个对象的问题；本项也不声称小型数据库已有明显延迟，未测量实际元数据 IO、分配或端到端时间。

不同于 PERF-39 的 MCP 远端分页工具目录，本项是代码生成模块当前数据库的元数据解析；也不同于 PERF-10/25 的检查点正文与 Git 进程等待。后续处理方向：为单项解析提供参数化的名称/类型查询，只返回目标对象的受控结果，保留真正目录浏览所需的列表路径。必须保留当前安全标识符、当前数据库/固定 schema、基础表与视图区分以及 ordinal 精确匹配语义；双库默认排序规则可能与 ordinal 不同，不能只替换为大小写不敏感的等值条件就改变是否找到对象的结论。保持 HostOnly、精确权限、取消、表不存在与对象不存在错误差异，以及只生成草案不执行 DDL 的边界；不能为减少读取复用长期陈旧目录而接受已经删除或变更类型的对象。

待验证场景：

- 双库包含较多无关表/视图时，读取已知表、已知视图、缺失对象、列同步和迁移草案，比较返回元数据行/字节、往返、分配及延迟，确认单项解析不再物化全目录。
- 覆盖大小写不同名称、不同 schema/数据库同名对象、非法名称和视图冒充基础表；保持现行匹配、错误码、Host 与权限结论。
- 对象在解析与列读取之间删除/变更、请求取消及数据库失败时返回稳定结果；列类型映射、人工 UI 配置合并和双库草案内容保持正确，不执行 DDL。

## PERF-74：文档版本历史全量读取与渲染

**优先级：P2。第三十轮新增。**

代码位置：

- [HostDocumentItemQueryService.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemQueryService.cs)，第 145–175 行先通过文档读取策略与活动文档校验，再查询并映射此文档的全部版本，返回 IReadOnlyList，没有分页或结果数量预算。
- [DocumentItemSql.cs](../../src/Modules/Full.NET.Modules.Document/Persistence/DocumentItemSql.cs)，第 500–511 行 ListVersionsByItemId 只按 DocumentItemId 过滤、按 VersionNumber 倒序，既无 TOP/LIMIT 也无版本游标。投影是 FileId、版本号、Hash、大小、说明和上传人等元数据，不包含原始文件二进制或完整 Office 正文。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/Endpoint.cs)，第 59–74 行版本目录接口返回完整列表，要求文档读取权限，没有页大小或游标参数；[HostDocumentItemsView.vue](../../ui/admin/src/views/HostDocumentItemsView.vue)，第 596–602 行打开抽屉完整加载，第 641、673 行回退和删除后重新加载全部版本，第 1105–1170 行 el-table 直接使用完整 versionHistory，没有分页或虚拟化。
- [DocumentVersionRetentionOptions.cs](../../src/Modules/Full.NET.Modules.Document/Configuration/DocumentVersionRetentionOptions.cs)，第 13–14 行允许 MaximumRetainedHistoryVersions=0，默认值也是 0；[DocumentVersionRetentionRunner.cs](../../src/Modules/Full.NET.Modules.Document/Retention/DocumentVersionRetentionRunner.cs)，第 34–37 行此配置下跳过自动裁剪。[HostDocumentItemManagementService.cs](../../src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemManagementService.cs)，第 433–456 行新增版本没有以版本总量作为写入上限。

在关闭自动裁剪的合法配置下，同一文档可以持续累积历史版本。每次打开版本目录都读取、序列化、传输并渲染全部历史，回退和删除后的完整刷新又重复这一成本；成本随版本数量增长，抽屉显示区域的大小不限制表格节点数。即使启用了保留策略，后台分批裁剪也不是列表请求的返回上限。此处没有声称每次会下载所有版本文件，也没有把允许保留历史本身判为错误；未测量实际文档规模、响应字节、浏览器长任务或内存峰值。

本项与 PERF-14 的 GoView 画布、PERF-31 的打印正文、PERF-59 的报表快照列表涉及独立实现，修复它们不会覆盖文档版本接口。后续处理方向：版本目录使用受控页大小和稳定的版本号/Id 游标，客户端按页请求并渲染，写后更新必要记录或相关页；必要的当前版本定位独立提供，不依赖客户端预先拥有所有历史。保留 DocumentAccessPolicy、Host 边界及预览/回退/删除各自精确权限，当前版本和最小保留量仍由服务端守卫；不能为了减少读取自动删除用户合法保留的历史，也不能仅截取最新 N 条却不给旧版本继续读取入口。

待验证场景：

- 双库覆盖少量、大量历史及裁剪关闭/开启，记录每次返回行数/字节、SQL、分配与浏览器渲染成本，确认打开抽屉不再随完整历史线性加载。
- 新版本并发上传、回退当前指针、删除历史及后台裁剪期间，翻页稳定且没有重复/漏项；当前版本可识别，旧版本仍可定位，写后刷新不重新拉取全部历史。
- 无读取/回退/删除权限、跨文档版本 Id、并发 Version 冲突、取消和最小保留保护继续正确；列表分页不得成为读取已无权文档历史的旁路。

## PERF-75：Host 文件名包含查询缺少候选范围预算

**优先级：P2。第三十轮新增。**

代码位置：

- [HostFileSql.cs](../../src/Modules/Full.NET.Modules.Files/Persistence/HostFileSql.cs)，第 51–62 行目录限制由 ApplyFolderFilter 控制，文件名条件分别是 OriginalFileName LIKE '%' + @FileNameContains + '%' 和 LIKE CONCAT('%', @FileNameContains, '%')；第 65–109 行 COUNT 与分页列表都执行这一包含条件，没有创建时间范围或专门的包含搜索访问路径。
- [HostFileQueryService.cs](../../src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileQueryService.cs)，第 22–51 行虽将页大小限制在 100 内，每次仍先执行精确 COUNT 再读取当前页；第 206–213 行省略 folderId 得到 All，第 238–258 行 All 关闭目录过滤，文件名仅做 trim 和不超过 128 字符的规范化，没有候选数据量/时间窗要求。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/Endpoint.cs)，第 21–48 行目录与文件名都是可选参数，读取权限通过后可发起无目录限制的包含搜索；[HostFilesView.vue](../../ui/admin/src/views/HostFilesView.vue)，第 198–214 行发送目录及文件名条件、采用服务端分页，本项不将其误报为浏览器拉完全部分页。页面部分状态会限定单个目录或未分类文件，风险按省略目录或候选目录较大的场景评价。

在文件元数据量较大、合法请求没有指定目录时，搜索少见或不存在的文件名片段仍要判定整个活动 Host 文件候选集。前导通配符不能直接转化为普通 B-tree 的文件名前缀范围；每页前置精确 COUNT 的成本也不会被 PageSize=100 或当前页只有几条命中约束。即使目录过滤开启，单个大目录内仍没有时间或候选规模预算。现行 SQL 只筛选 ready、未删除、TenantId IS NULL 的元数据，这些状态与作用域过滤仍然有效，不应称为无过滤全库查询。本项登记潜在扫描和重复统计风险，未获取实际双库执行计划、逻辑读或延迟，不能断言所有搜索都会全表扫描。

本项与 PERF-16 的 MQTT 历史主题查询属于相似风险模式，但使用独立的 Files SQL、筛选契约和客户端入口，不能由其修复自动覆盖；也不同于 PERF-01 的后台文件扫描游标、PERF-02 的机构参考参数膨胀和 PERF-20 的日志前端全量加载。后续处理方向：按真实数据及计划明确包含搜索的候选范围和查询预算，例如受控目录/时间范围及可说明的总数契约，并评估适合文件名匹配的双库访问路径。精确、前缀、包含若采用不同策略，应在产品契约中明确，不能静默把包含改成前缀或截断候选后谎报总数；单加普通文件名索引、提高命令超时或缩小响应页数都不能独立证明候选扫描已受控。不因本项擅自引入未批准搜索设施，继续保持 Host/租户隔离、精确权限、参数化 SQL、ready/软删除和目录语义。

待验证场景：

- 双库按代表性文件量及目录分布执行全范围与目录内包含搜索，覆盖高命中、少命中、无命中及较深页，记录 COUNT/列表各自计划、逻辑读、排序、延迟和取消响应。
- 验证省略目录、未分类目录、具体目录、时间/候选预算边界以及中文文件名的匹配和总数契约；如改变返回总数方式或搜索类型，客户端必须明确表达且不丢失继续查找能力。
- 并发搜索及文件上传/移动/软删除时，不泄露租户文件或返回未 ready 对象，稳定排序及分页语义不被优化破坏；取消和预算拒绝不以放宽隔离或后台继续无界查询兜底。

## PERF-76：Jobs 终态历史清空没有单事务工作预算

**优先级：P2。第三十一轮新增。**

代码位置：

- [HostJobExecutionQueryService.cs](../../src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobExecutions/HostJobExecutionQueryService.cs)，第 82–99 行 ClearAsync 使用 ICommandTransaction.ExecuteResultAsync 包裹 ClearCoreAsync，后者只执行一次 ClearExecutionsByDefinition 后返回成功，没有批大小、截止时间或继续处理状态。
- [JobSql.cs](../../src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs)，第 967–977 行 DELETE 按 TenantId IS NULL、JobDefinitionId 和 succeeded/failed/cancelled 过滤，没有 TOP/LIMIT、已冻结的历史范围或分批游标。该共享语句在 SQL Server/MySQL 上都删除当前匹配的全部终态行。
- [Endpoint.cs](../../src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobExecutions/Endpoint.cs)，第 69–93 行 POST /clear 接收单个 jobDefinitionId，要求 ExecutionsClear 精确权限，服务返回成功后响应 204；没有批量清空进度或部分完成契约。
- [SQL Server 030_JobsDefinitionAndExecution.sql](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/030_JobsDefinitionAndExecution.sql)，第 497–501 行已有作业/创建时间索引及 pending 租约索引；[MySQL 同名迁移](../../src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/030_JobsDefinitionAndExecution.sql)，第 33–34 行也有对应查询索引。因此本项不是缺少 JobDefinitionId 访问索引，而是命中全部历史后的删除工作量及事务边界。

同一作业长期定时执行并积累较多成功、失败或取消记录后，一次清空会在一个请求和事务内删除全部匹配历史。即便索引能快速定位这些行，删除、索引维护、事务日志及必要回滚的规模仍随匹配历史增长；这可能增加锁与连接持有时间，并与同表执行状态写入、领取或普通查询争用资源。请求取消或命令超时不等于事先限定事务的行数/日志工作预算，失败后的回滚也可能继续占用资源。此为静态事务范围风险，未执行真实大批删除、锁升级、死锁或日志容量实验，不能宣称上述现象已实际发生。

现有 SQL 明确保留 pending、running、cancelling，且只删除指定作业的 Host 终态记录；不把它误述为删除运行任务或所有租户历史。它不同于 PERF-23 的派发/执行耦合、PERF-67 的 Worker 实例观测历史，也不是 PERF-08 的 OIDC 自动保留清理；这些旧项及已有 Outbox 有界保留实现都不会改变本接口的单事务删除。

后续处理方向：为同步清空定义可接受工作预算，超出预算时采用有明确完成状态、可取消/恢复的受控清理，并冻结该次清空的目标边界，避免持续新终态记录让清理无法收敛。双库按稳定主键及终态条件分批删除、每批独立提交，评估现有索引与批次的锁/日志成本；仅在同一个外层事务内循环 TOP/LIMIT 仍然会累计长事务，不能作为边界修复。不得只删一批就返回现行“清空完成”的 204，也不能为加速改为 TRUNCATE 或放宽 Host、作业和终态条件；新的部分完成/恢复语义须同步接口、客户端及测试，并保留必要诊断、去重和执行恢复事实。

待验证场景：

- 双库准备少量及大量终态历史并混入 pending/running/cancelling，比较每批行数、事务时长、日志量、锁等待与其他任务领取/续租尾延迟，确认单事务工作受预算限制。
- 清理期间并发新建执行、终态写入、重试与取消，不能删除仍在运行或重新进入可执行状态的记录；其他作业、租户记录与计划定义保持完整，目标边界最终收敛。
- 请求取消、中途批次失败、进程重启及重复清空保持可说明的完成/恢复状态；前端不得把部分成功显示为全部完成，不以无限重试掩盖实际数据库失败。

## PERF-77：导入及报表导出缺少执行期间的租约维持与失租停止

**优先级：P2。第三十二轮新增。**

代码位置：

- [ImportExportTaskRunner.cs](../../src/Modules/Full.NET.Modules.ImportExport/ImportTasks/ImportExportTaskRunner.cs)，第 361–398 行领取时一次设置 LeaseExpiresAtUtc；第 151–170 行将请求/宿主 cancellationToken 直接传给 ExecuteBatchAsync，没有按租约到期时间建立执行期限，也没有执行期间续租或失租通知。[ImportExportTaskSql.cs](../../src/Modules/Full.NET.Modules.ImportExport/Persistence/ImportExportTaskSql.cs)，第 22–25 行明确允许 executing 且租约到期的任务重新领取，第 129–174 行是双库领取实现，第 187–205 行直到提交进度时才通过 LeaseId 拒绝旧所有者更新。
- [ImportExportOptions.cs](../../src/Modules/Full.NET.Modules.ImportExport/Configuration/ImportExportOptions.cs)，第 22、25 行默认批次 50 行、租约 120 秒；[ImportExportOptionsValidator.cs](../../src/Modules/Full.NET.Modules.ImportExport/Configuration/ImportExportOptionsValidator.cs) 允许批次最多 200 行、租约最短 30 秒，没有批次时长与租约约束。[TenantPositionsStaticImportSchemaHandler.cs](../../src/Modules/Full.NET.Modules.Organization/Features/ImportExport/TenantPositionsStaticImportSchemaHandler.cs)，第 115–123 行逐行等待 ImportTaskRowAsync，处理上下文只绑定 TaskId，不接收调度租约。
- [ReportingExportTaskRunner.cs](../../src/Modules/Full.NET.Modules.Reporting/Features/ManageExportTasks/ReportingExportTaskRunner.cs)，第 55–61、110–117 行领取后执行，第 330–370 行设置一次租约；第 141–186 行检查已有输出、生成工作簿、上传及完成均使用调用方令牌，期间未维持租约或在到期/失租时停止新工作。[ReportingExportTaskSql.cs](../../src/Modules/Full.NET.Modules.Reporting/Persistence/ReportingExportTaskSql.cs)，第 27–30 行允许 processing 过期任务重领，第 235–275 行完成更新仍有 LeaseId 条件。
- [ReportingExportOptions.cs](../../src/Modules/Full.NET.Modules.Reporting/Configuration/ReportingExportOptions.cs)，第 21、40 行默认租约 300 秒、允许最短 30 秒；[ReportingExportWorkbookSource.cs](../../src/Modules/Full.NET.Modules.Reporting/Features/ManageExportTasks/ReportingExportWorkbookSource.cs)，第 92–101、160 行按页串行收集结果。[ReportingExportPolicy.cs](../../src/Modules/Full.NET.Modules.Reporting/Domain/ReportingExportPolicy.cs) 允许最多 5000 行、每页 200 行，最多可经历 25 页；[ReportingExecutionPolicy.cs](../../src/Modules/Full.NET.Modules.Reporting/Domain/ReportingExecutionPolicy.cs) 的 30 秒限制是单条外部数据库命令，不能约束所有页、渲染及上传的总时长。

多实例下，若一个合法导入批次或导出任务因多轮数据库等待、文件 IO、渲染或上传慢而超过 LeaseSeconds，其他 Worker 可按正常恢复条件重领同一任务，但首个实例未收到失租取消，仍继续工作。例如分页导出即使每页命令都未超时，累计时间仍可能超过默认 300 秒；导入的行数上限同样不等于批次耗时上限。第二个实例会重新读取尚未推进的检查点或在暂无就绪输出时重新生成报表，造成重复解析、幂等回执查询/竞争、数据库读取和文件生成/上传尝试，积压和资源压力较大时可能进一步放大恢复负担。此为静态多实例时序风险，未运行慢任务或真实租约竞争复现，不声称所有默认任务都会超时、必然留下重复文件或发生数据损坏。

已有保护必须保留：[TenantPositionManagementService.ImportRecovery.cs](../../src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositions/TenantPositionManagementService.ImportRecovery.cs)，第 19–60 行按任务/原始行号与载荷摘要读取回执，并以唯一占位与业务写入同事务保证行级幂等；ImportExport 进度及 Reporting 完成都有 LeaseId 守卫。报表还有先绑定既有就绪输出的恢复路径。这些保护可以防止部分重复副作用或旧所有者覆盖新租约，但不能消除重领之前及并发执行期间的重复工作；仅在执行入口检查一次已有文件也不能合并同时尚在生成的两个实例。

本项不同于 PERF-04 的 Webhook 整批提前占租：这里通常是单任务即时领取，问题发生在任务实际执行过久之后；也不同于 PERF-22 的 AI Worker 心跳或 PERF-23 的 Jobs 派发阻塞，后者已有执行续租。PERF-50 的工作簿重复解析会增加成本，但即使消除重复解析，其他慢数据库或存储路径仍可暴露本项，因此将两个同类 Runner 合并登记为独立问题。

后续处理方向：明确完整执行预算与租约关系，采用受控续租/所有权监视，或在租约安全期限内停止并交还检查点；续租必须以可信租户、任务、状态和 LeaseId 为条件，不能延长已被接管的租约。若续租与业务事务并行，应使用独立受控作用域及连接，避免共享 DbSession 或等待长业务事务后才续租；失租后停止发起后续行、页和上传，并观察、收尾已在途操作。不能只调大 LeaseSeconds、取消过期恢复、无限续租无进展任务，或移除完成守卫和行级幂等来隐藏竞争；保留活动租户复核、精确权限/列投影、崩溃重领、取消与文件引用对账语义。

待验证场景：

- 双库以两个独立 Worker/作用域执行超过租约的慢导入与多页慢导出，记录同时执行数、续租时序、重复回执/查数及上传尝试；正常续租时不被抢领，不能用单实例测试证明不存在竞争。
- 首个实例丢失续租、数据库暂时失败、租户停用或宿主取消后，应停止后续工作；其他实例接管后，旧 LeaseId 不能更新进度/终态，已提交导入行仍按原摘要幂等恢复。
- 覆盖生成尚未上传、上传成功但完成未提交、续租与完成竞争及真实崩溃；恢复能绑定正确已有文件、资源可对账，且不会无限延长无进展任务或误报执行已被取消。

## PERF-78：Shadow 事件去重状态没有容量或观测窗口

**优先级：P2。第三十三轮新增。**

代码位置：

- [ShadowEventComparisonProcessor.cs](../../src/Hosts/Full.NET.Host.Worker/ShadowEventComparisonProcessor.cs)，第 45–48 行只在 Shadow 与 Kafka 均启用时继续执行；第 57 行在消费循环外创建 seenEventIds，第 59 行起循环持续到宿主取消，第 112–115 行对每个新 EventId 执行 Contains/Add，整个执行过程没有 Remove/Clear、到期、数量预算或观测轮次结束边界。
- 同文件第 118–127 行把 duplicate 传入比较器，第 137 行提交 Kafka 位点；提交位点不会释放 seenEventIds 内历史标识。集合只保存 Guid，不保存完整事件 Payload，本项不将全部消息正文驻留作为内存增长前提。
- [ShadowComparisonOptions.cs](../../src/Hosts/Full.NET.Host.Worker/ShadowComparisonOptions.cs)，第 18、23、28、33 行配置仅包含启用、Topic 前缀、组名及 PollTimeout；校验器也没有事件数量、内存或持续观测时长预算。PollTimeout 约束一次空轮询，不限制整个消费循环的生命周期。
- [Program.cs](../../src/Hosts/Full.NET.Host.Worker/Program.cs)，第 135–146 行在 ShadowCdc 或显式启用时注册此 BackgroundService；[appsettings.json](../../src/Hosts/Full.NET.Host.Worker/appsettings.json)，第 71–76 行默认 Enabled=false。[ShadowEventComparison.cs](../../src/BuildingBlocks/Full.NET.Messaging.Abstractions/ShadowEventComparison.cs)，第 369–378 行 duplicateObserved=true 会直接返回 DuplicateObserved，它参与对比结论，不能当作可随意丢弃的性能缓存。

明确启用 Shadow 后，持续输入不同有效 EventId 时，集合数量按本次 Worker 运行期间的累计不同标识 U 增长，内存复杂度为 O(U)，与当前 Kafka 在途消息数、已提交位点或积压是否清空无关。长期影子观测、回放或容量演练可以持续扩大这部分驻留，并在扩容集合时增加分配/GC 压力；即使事件已被判为匹配或不匹配，标识也不会从集合移除。宿主退出后局部集合可释放，并非跨重启持久化增长。此为静态状态生命周期风险，未运行真实长时消费，未测量单标识实际开销、峰值或吞吐，不能断言已发生 OOM。

本项是影子证据路径的资源边界，不是正式消费者的 Inbox 幂等状态，也不是 PERF-06 的消费组启动、PERF-24 的回放分区停读或 PERF-28 的签名 Nonce 回收；改变这些旧项不会限制当前 HashSet。默认关闭且不调用业务 Handler 的事实降低了常态暴露，但不限制正式开启观测后的累计事件量。

后续处理方向：为一次 Shadow 证据运行定义明确的事件数、持续时间及资源预算，并保存所覆盖的 Topic/Partition/位点区间与完成状态；预算不足时明确终止或标记证据不完整，不能清空/淘汰旧 EventId 后继续把未检测到重复解释为匹配。若约定的完整观测窗口不能放入有界内存，应评估窗口所属的受控精确去重存储及其保留/恢复预算，不能用存在误判的近似集合直接替代验收证据，也不能把业务 Inbox 或 Outbox 改成这类临时状态的无界存储。保持重复、指纹不一致、缺失来源与位点异常的可区分结论，以及 ADR-0006 的单一发布所有权和正式消费至少一次语义。

待验证场景：

- 在明确启用的 Shadow 场景持续输入不同标识，覆盖预算边界及超限，记录集合/存储数量、托管堆、分配、GC 与消费速率；已提交消息或空积压不能使资源无限继续累计。
- 覆盖窗口早期/晚期重复、多 Topic/Partition、内容不一致及缺失 Outbox 来源；资源策略不得漏掉约定窗口内的重复或把证据不足显示为 Match。
- 宿主停止、重启、再平衡和回放时清楚说明观测区间及去重连续性；默认关闭时不启动这条消费路径，任何改动均不调用正式业务 Handler 或改变 Inbox/发布所有权。

## PERF-79：HTTP Job 错误正文读取脱离请求超时

**优先级：P2。第三十四轮新增。**

代码位置：

- [HttpJobExecutor.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/Handlers/HttpJobExecutor.cs)，第 84–94 行创建 timeoutSource、按 TimeoutSeconds 设置 CancelAfter，并将其令牌用于 ResponseHeadersRead 模式的 SendAsync；第 102–107 行调用 BuildFailureSummaryAsync 时却传入原始 cancellationToken，没有继续使用该超时令牌。
- 同文件第 19 行将 MaxErrorBodyBytes 固定为 2048，第 134–139 行取得正文流后只执行一次至多 2048 字节的 ReadAsync，但两步都只收到原始执行令牌。第 145–154 行仍可传播调用方取消并在普通读取异常时回退状态行，第 157–159 行将摘要文本限制到 256 字符；这些已有字节/文本边界不能代替首次读取的时间预算。
- [HttpJobArgsValidator.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/HttpJobArgsValidator.cs)，第 139–142 行允许 TimeoutSeconds=1～120；HttpJobExecutor 默认采用 30 秒。[JobsModule.cs](../../src/Modules/Full.NET.Modules.Jobs/JobsModule.cs)，第 131–134 行注册工厂客户端与 SSRF Handler，没有另加错误正文读取期限。
- [JobExecutionRunner.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs)，第 316–333 行建立执行取消源、轮询显式取消并等待 Handler，没有独立 CancelAfter；第 227 行起按租约续租，正常续租成功本身不会结束停顿中的 Handler。[JobsWorkerOptions.cs](../../src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs) 默认 MaxConcurrency=1，因此此等待会占用当前唯一执行槽位。

若允许访问的 HTTP 目标快速返回不在成功白名单内的状态头，随后保持连接、既不发送正文首字节也不结束正文，SendAsync 可以在请求超时前返回；接下来的 ReadAsync 却不观察 timeoutSource.Token。ResponseHeadersRead 下，发送阶段完成不等于正文读取完成，HttpClient 的发送时限也不能替代传给正文操作的取消预算。结果是配置的请求超时到达后仍可能等待错误摘要，直到显式执行取消、租约续租失败、宿主停止或下层网络结束。即使上游错误状态已经明确，诊断读取也会延迟失败收敛和释放执行槽位，影响后续任务处理。

本项不是无界正文分配：当前只读取一次，最大 2048 字节，若读取及时返回任意一段正文就会继续处理，不要求填满缓冲区；主要触发条件是首次读取停顿，而不是收到首段后一直等待完整错误页面。未执行慢服务器复现或吞吐测试，不能量化实际阻塞时长或损失。SSRF、凭据引用、成功状态白名单以及正常成功响应提前返回均是现有有效边界。

它与 PERF-11 的 OSS 错误正文属于相似的期限风险，但涉及独立 HTTP Job 客户端及令牌传递，且这里已有小字节预算；PERF-43/70 的完整响应缓冲也不是本调用形态。PERF-23 的计划派发与整批执行耦合会放大等待影响，但即使拆开派发，这个 Handler 仍会占用执行槽位，因此单独登记。

后续处理方向：让错误摘要获取沿用同一请求截止预算，必要时在剩余时间内再设置更短的诊断读取预算，超出后立即释放响应/流并按明确策略收敛失败；保持 2048 字节和安全摘要边界，不为修复时间问题改为完整缓冲。宿主/显式取消、请求超时及普通摘要读取失败应区分处理，不吞掉取消，也不因诊断读取超时新增盲目重试；保留 HTTP 方法、状态白名单、SSRF 地址绑定、租约终态守卫及既有重试/未知外部副作用语义。

待验证场景：

- 受控 HTTP 替身立即发送错误响应头后不发送正文也不关闭，覆盖配置超时 1 秒及正常默认值；确认 Handler 在明确期限内退出，响应与流释放，后续作业可取得槽位。
- 覆盖错误正文立即返回少量字节、EOF、首读前取消、中途网络异常、超长页面和成功响应；字节上限、摘要截断及成功状态语义保持正确，不额外等待完整正文。
- 在 Runner 中验证显式取消、宿主停止、续租失败和请求超时的终态/重试分类，旧租约不能完成新执行；同时比较单并发及有界多并发下槽位占用与积压，而不只测独立 HttpClient 调用。

## PERF-80：流程设计器重复深度监听子树并整树同步

**优先级：P2。第三十五轮新增。**

代码位置：

- [nodeWrap.vue](../../ui/admin/src/workflow/vendor/workflow-vue3/src/components/nodeWrap.vue)，第 76、90 行分别递归挂载分支及 childNode 的 nodeWrap；第 759–765 行每个实例均以 deep=true 监听完整 props.nodeConfig，回调执行 refreshNodeError。因此祖先的监听范围包含全部后代，而非只有当前节点的可编辑字段。
- 同文件第 15–25 行节点名称输入直接 v-model 到 nodeConfig.nodeName，普通编辑即可改变后代对象；第 671 行起 refreshNodeError 处理节点校验，并在第 751–752 行按差异写入 error/errorTip。这里已有值相等时不重复赋值的保护，不能把所有校验回调描述为必然再次修改对象。
- [WorkflowVue3Designer.vue](../../ui/admin/src/workflow/WorkflowVue3Designer.vue)，第 101–116 行分别深度监听外部 modelValue 和内部 nodeConfig；外部同步克隆整树，内部同步先比较两棵树的可持久化 JSON，再克隆并上抛。第 449–460 行克隆实际是 JSON.stringify/JSON.parse，比较也执行 JSON.stringify。syncingExternalModel 与 transientDesignerKeys 已阻止等价/瞬时状态形成循环，但没有让普通编辑的监听、比较和复制变成局部操作。
- [WorkflowDefinitionsView.vue](../../ui/admin/src/views/WorkflowDefinitionsView.vue)，第 627–633 行通过 v-model=workflowTree 接入上述设计器，所以上抛模型会继续进入父子同步路径，而不是仅存在于未使用的复制组件中。

链式树有 N 个递归实例时，各实例深度监听的子树大小依次为 N、N-1、…、1。初始化收集依赖，以及末端字段变化后相关监听重新遍历时，累计节点访问规模可达 N(N+1)/2；根组件的整树比较、克隆及外部模型替换还会增加工作与临时字符串/对象分配。分支较浅时不应机械套用这一最坏形态，但编辑深处节点可能让本来只需校验本节点的多个祖先重复访问后代。此处是静态调用关系和有界机制诊断支持的放大风险，未测量实际页面的帧率、长任务、堆峰值或键入延迟。

局部诊断（2026-10-20）：在 ui/admin 下以 `node --input-type=module` 从标准输入执行纯内存夹具，直接导入已安装 Vue 3.5.42；分别构造 20/40/80 个 childNode 链，将 nodeName 定义为计数 getter，每个节点注册与源码相同形态的 `watch(() => node, () => {}, { deep: true })`。记录注册时读取数，再清零计数、修改末端 nodeName 并 await nextTick，最后停止全部监听。原始输出：

```json
{"vue":"3.5.42","scope":"bounded reactive-chain diagnostic; no browser or application benchmark","results":[{"nodes":20,"mountReads":210,"leafEditReads":211},{"nodes":40,"mountReads":820,"leafEditReads":821},{"nodes":80,"mountReads":3240,"leafEditReads":3241}]}
```

末端修改计数还含代理写入时的旧值读取，不能把它解释为校验函数调用次数。夹具没有挂载实际组件，也没有执行根组件 JSON 克隆、DOM 渲染或 HTTP 请求；仅验证递归子树 deep-watch 这种机制的访问放大，不是应用性能基准或回归通过证明，没有生成仓库测试产物。

本项不同于 PERF-32 的服务端图编译递归/输入规模预算和 PERF-62 的后端办理人解析；即使图有规模上限，合理规模内的重复依赖遍历与整树同步仍存在。后续处理方向：节点校验只跟踪自身实际依赖的字段及明确的关联节点，结构变更和跨节点规则由有界的集中校验处理；父子同步使用明确的编辑版本或局部变更契约，减少普通输入的整树比较与 JSON 克隆。保留现有防循环回写、瞬时字段不入库、纯 JSON 边界、外部替换和禁用状态，以及白名单节点/条件规则；不能简单移除深度校验而漏报配置错误，也不能仅加防抖后声称已消除遍历复杂度。

待验证场景：

- 实际设计器覆盖链式、平衡分支及混合树，编辑末端名称、办理策略和条件，记录依赖遍历/校验次数、整树序列化次数、浏览器长任务、键入延迟和分配；区分根同步与节点校验成本。
- 节点新增、删除、分支重排/复制、外部模型替换、挂载瞬时字段补齐及抽屉开关后，校验与保存草稿保持正确；无无限回写，外部对象与内部编辑隔离不被破坏。
- 禁用编辑、未批准节点、错误配置和权限/作用域切换继续拒绝或清理，导出的草稿契约及服务端发布结论不变；源码 Unit 与真实页面验证不能由上述内存夹具替代。

## 本轮实施与集中验证（2026-10-21 完成）

用户已授权批量修复全部 80 项，其中 P1 为 13 项。实现与回归用例已连续推进，完整过程见[批量计划](../superpowers/plans/2026-10-20-performance-backlog-remediation.md)。本轮修复及本地受影响验收已完成，原始失败、定向复测与既有环境跳过分别保留，证据和边界如下。

已取得的集中证据：

| 范围 | 实际结果与边界 |
| --- | --- |
| Vue 组件用例 | 先前完整 214 文件 / 986 项通过；补匿名预览和角色候选两组边界后，实际相关 6 文件 / 26 项通过，包含旧轮询竞态先 RED 后 GREEN、新 URL 不得撤销、跨页选择及精确部分保存 |
| 共享客户端契约 | 最新 67 文件 / 292 项通过，包含匿名请求人为 null 的守卫 |
| 客户端构建与预算 | 匿名预览竞态修正后的类型检查、Vite 生产构建及全部包预算通过；首屏静态 JS 1,494,886 bytes / gzip 390,378 bytes，预算未上调 |
| OpenAPI | 离线 213/213，生成产物 `--check` 零漂移；实际运行时导出已覆盖双库 |
| 后端 Unit | 最新完整 8997 个发现 UID：8996 Passed、0 Failed、1 个 Linux 专用 NotExecuted，入口退出 0；严格核对完整 XML/UID/summary/程序集 SHA，包含 SSE 五项和配额物化六项；后续成员查询修正的相关Service定向7/7另行通过（含1个新Unit方法），未宣称完整8998重跑 |
| Architecture / 分析器 | 先前完整 238/239 后剩余 SQL 登记定向 2/2；最新 API Native AOT 影响集 74/74，完整 XML/UID 一致。API/Worker AOT 分析构建均 0 警告/0 错误，实际 Linux 原生运行已按下行影响范围补测并合并 |
| 双库 273–276 恢复 | SQL Server / MySQL 同场景 2/2 通过：预算计数重建、删除后归零、索引/列恢复、投影单调版本、匿名请求人可空及租户隔离 |
| Integration 分片 / 模块 | canonical 实际发现 1344 项，迁移集 562 项，全部 UID 无遗漏/重复；模块原始 513 项与定向复测按 UID 合并为 511 Passed、2 个外部短信/SMTP 未配置 NotExecuted。原始失败命令保留非零事实，合并不是原命令重跑通过 |
| 消息 | 按全部 80 个精确 UID 严格合并为 74 Passed、0 Failed、6 个既有 SQL Server Agent-gap NotExecuted；原 v4 的 73/1/6 与单项诊断失败记录仍保留，合并不是原命令重跑通过。最终延迟类 3/3（0 跳过）及完整 XML/UID、前后输入哈希均通过；串行 ES 收据使用既有 64 条有界配置，不能称原 8 条串行配置已通过 |
| Native 运行 | 原核心66项为64 Passed/2 Failed/0跳过，原命令非零与Failed summary保留；成员查询修正后Unit定向7/7、JIT实际HTTP双库2/2通过。重新发布API/Worker并构建Linux测试程序集后，双库critical 2/2（2m38s）和Replay 2/2（2m35s）均通过，零跳过/失败；与原64个Passed UID无交叠合并为完整68项。原66命令未改记为通过 |
| 兼容性 | 正式 `pnpm test:dotnet:compatibility` 实际 12/12，通过且 0 跳过；Release 构建 0 警告/0 错误 |
| 浏览器 | 真实 SQL Server 16/16、MySQL 16/16 均通过，零跳过/失败/重试；精确同一用例集合，覆盖用户、角色、分享的真实 Vue/API 生命周期及拒绝边界，前后源码哈希一致。模拟栈原 97/1/4 与旧组织响应修正后该用户流程定向 1/1 的事实保留 |

独立只读复核发现并修正了角色双入口并发、Kafka 消费组失败联动取消、迁移重放恢复及原生物化器缺口；匿名预览旧轮询清掉新 URL 的竞态已实际复现并修正，回归与邻近用例通过，最终差异亦经只读复核。完整消息重型路径、真实双库浏览器及Linux Native按影响范围补测合并均已取得上述证据；80项状态据此关闭。Native最终源码聚合为`9ca06d088059d9432f059797e9831864d31d74be37403fc08ddb07a0f3525fcd`，发布清单仍保留原始构建HEAD与二进制SHA。

局部效果证据来自通过的双库 Performance 场景（Release / .NET 10.0.13 / 测试进程 CPU=2，同机 i7-12700H、64 GiB，五个计时样本）：四项权限授权由每边界 8 次读取变为 2 次；36 个流程实例、36 个步骤、108 条开放恢复任务的扫描由 108 次无效插入变为 0 次，SQL Server 中位耗时 111.71→8.92ms、MySQL 176.90→6.51ms。原始 TRX 同时保存双库编译计划/EXPLAIN 与隔离断言。该小夹具不是吞吐、尾延迟或生产容量实测，保持 `Capacity-not-verified`。

## 集中处理与验收约束

- 本轮已获授权并优先处理 P1（13 项），连续完成关联 P2 后集中验收；保留原始问题与历轮审查证据，不以实施记录代替通过证明。
- 保持授权、可信租户隔离、会话撤销、字段投影、租约与版本隔离语义，不以陈旧缓存或失败放行换取性能。
- 明确回归场景后集中执行受影响验证；成对覆盖双库，涉及 Vue 的改动覆盖页面流程，涉及 Host.Api 可达路径或绑定的改动按规则验证 Native AOT。
- 效果比较须记录相同环境、数据规模与原始结果；未取得实测前不承诺固定吞吐、尾延迟改善或容量达标。
- 后续关闭各项时记录实现位置、实际验证命令与结果、跳过或未验证范围，不以勾选代替证据。

- 本轮模块运行定位到 PERF-34 的真实缺陷：分页查询以 Guid.Empty 作为缺失指标和首批游标参数，被全局 AssignedGuidTypeHandler 正确拒绝并导致 HTTP 500。已改为独立 nullable Id 投影、双库带类型 NULL、可空首批游标及对应静态 AOT 物化器；显式空 UUID 游标在查询前返回 400，不放宽全局标识约束。Unit 保留用量和 CAS 断言，并补首次读取参数/无效游标零额外查询；Native critical flow 补缺失指标、至少两页真实游标、应用后幂等及 400。六个原有配额双库场景加入定向集合；后续配额物化六项 Unit 与双库六项场景实际通过，相关复测已纳入上述模块 UID 合并证据，本次双库Native critical 2/2已实际覆盖配额游标、幂等及拒绝边界。

### 验证入口与原始记录

本批使用系统临时目录 `C:/Users/Administrator/AppData/Local/Temp/fullnet-review-native-2gnp8ed6` 保存必要原始 TRX、精确 UID 发现清单、输入哈希、合并说明和本任务临时编排入口。入口均调用仓库 `test-run-context.mjs`，复用既有构建并遵守 root/global-heavy 锁与资源余量；不复制整仓。下列为实际执行记录，不表示所有原始入口均退出成功。

| 实际入口 / 检查 | 结论 / 记录 |
| --- | --- |
| `node <临时目录>/fast-remainder-backlog.mjs` | 退出0；完整 Unit 8996 Passed/1 Linux 专用 NotExecuted，API AOT Architecture 影响集及 API/Worker 分析器通过；`backlog-fast-results` 保留严格校验后的原 TRX |
| `node <临时目录>/merge-messaging-evidence.mjs` | 退出0；`backlog-messaging-accepted-union.json` 按当前80个唯一UID合并为74/0/6，原失败记录不变 |
| `node <临时目录>/latency-followup-backlog.mjs` | 退出0；请求延迟整类3/3，`latency-followup-results/331d2a23-8cc5-4a28-a0b0-89c969696818` 保存TRX及三份实际报告 |
| `node <临时目录>/browser-backlog.mjs` / `node <临时目录>/verify-browser-results.mjs` | 两者退出0；`backlog-real-browser/validated-summary.json` 为双库32项严格结果，零跳过/失败/重试 |
| `pnpm test:dotnet:compatibility` | 退出0；12/12、零跳过，原始日志位于系统TEMP的`fullnet-performance-20261020-compatibility-final.log` |
| Vue 定向 Vitest、`pnpm --filter @fullnet/admin build` / `pnpm test:bundle-budgets` | 最新6文件26/26；类型、构建与预算均通过，日志`fullnet-performance-20261020-frontend-boundary-{final,build}.log`保留；先前完整986项未重复冒充新增用例的全集结果 |
| `node <临时目录>/build-backlog.mjs` | 新源码9ca06d…真实发布退出0：API ELF 141,148,624 bytes、Worker ELF 89,727,112 bytes；9/7既有批准告警、ELF/SHA和源码哈希均通过，Linux测试构建0警告/0错误（4m14s）。此前4/6/8 GiB不足及原9.5 GiB成功记录分别保留 |
| `git diff --check` / `git branch --show-current` / `git rev-parse HEAD` | 收尾核对分支main、源码与文档空白；提交前后按固定7442路径及删除标记核对工作区字节；验收后仅清理braces文件的一个末尾空行，单独保存原/新SHA与两字节差异，再逆向复验Native验收输入9ca06d…；Git索引移除3条已删除路径不构成实现变化。提交SHA与前后对应证明保存在`commit-all-source-proof.json` |

### 最终补测与资源回收

- `node <临时目录>/run-native-followup-with-power.mjs verify-native-query-followup.mjs --observe-native`实际退出0；Native、临时防休眠进程和数据库所有权观察器均退出0，70条精确所有权记录完整。`backlog-native-runtime-summary.json`明确为64个旧Passed加新4个Passed的68 UID证据合并，原66项失败TRX的SHA和Failed结论不变。
- `node <临时目录>/cleanup-final-backlog-v2.mjs`实际退出0；直接证明的SQL Server 36库、MySQL 38库全部删除，零跳过，独立查询确认均零剩余。此前模块批次249/247库的零剩余记录分别保留；未知或既有共享资源未纳入清理。
- 已回收本任务Native编译树、双角色发布目录、Linux测试构建树、运行工件及3个有精确生成证据的日志工件目录；保留必要原始TRX、失败记录、清单、SHA和精简结果。
- 验收是本机规定影响范围的验证；外部SMTP/短信未配置、SQL Server Agent-gap和Linux专用Unit的既有NotExecuted按表内事实保留，容量状态仍为`Capacity-not-verified`。

- 提交前暂存检查定位到新增`packages/braces/lib/stringify.js`的一个末尾空行；只删除最后一个CRLF，`node --check`通过。该机械格式调整不改变JavaScript token，未触发重复功能全测；`precommit-whitespace-normalization.json`及提交证明分别记录验收输入与提交输入，不改写Native发布SHA。
