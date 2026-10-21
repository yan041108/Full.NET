# 后台消费者与行政区域读取

`Workflow:RecoveryWorker:ScanIntervalSeconds` 默认 15，启动校验范围为 1–3600 秒。每类恢复来源最多返回 `BatchSize` 个尚未占用的候选；已存在 pending、failed、dead_lettered 任务时不再重复入队。占用查询按迁移 110 的生成列匹配，唯一索引维持并发占用约束，插入端仍保留并发占用保护。扫描未满后按间隔重新发现，扫描故障也退避；消费恢复任务的满批续跑独立于发现间隔。

`Notifications:Worker:MaxConcurrency` 默认 1，范围 1–16 且不能超过 `BatchSize`。大于 1 时每条投递使用独立作用域、租户上下文、连接和事务；完成顺序不保证与领取顺序一致。准备前、外发前仍检查租约，终态仍比较持有者、代次和修订号。增加并发前应按副本数核对数据库连接预算与提供程序限流；本轮没有提高默认并发或生产容量状态。

Document 预览、ImportExport 导入、Reporting 导出取满当前有效批次时立即继续；空批、未满批次和故障按当前 `PollSeconds` 退避。取消仍由宿主令牌传播，外部调用的超时与租约约束保持独立。

行政区域管理页通过 children API 逐层展开，刷新或成功变更后清空旧树缓存并取消旧请求。显式 tree API 在数据库内限制请求父节点及相对深度（1–5），最多返回 10,000 节点；超过预算以 `regions.administrative_region_tree_too_large` / HTTP 422 拒绝，应缩小父节点或深度。

AI 知识授权在每个原有检查点一次读取会话和权限快照，并要求整个集合通过；后续检查点重新读取权威状态，不跨边界缓存授权结果。

ImportExport 与 Reporting 按数据库原生 UUID 顺序保存进程内候选游标，每轮最多读取当前批次，空尾最多回卷一次。停用租户不领取、不改写原任务，但仍推进扫描位置；领取和执行前重新验证活动租户。游标仅负责公平性，重启可从头扫描，不承载授权、租户状态或租约。

Notifications 每波只领取 `MaxConcurrency` 个可立即执行的槽位，波次结束后才继续领取，整轮不超过 `BatchSize`。因此等待后续波次的任务不提前持有租约；原有续租、外发前核验、幂等键、超时和终态 CAS 继续生效。

Document 版本保留的 `BatchSize` 限制整轮实际尝试删除的历史版本数，失败尝试同样消耗预算。有效历史保留数取配置上限与 `MinimumRetainedVersionsPerItem - 1` 的较大值，避免不可删除候选持续占用首批。

Host 文件目录 `/api/v1/files/host-folders/tree` 返回 `parentId` 的直接子目录，省略参数只读根目录；节点用 `hasChildren` 表示可展开，不再携带递归 `children`。单父目录最多返回 1000 项，超过返回 `files.folder.query_limit_exceeded` / HTTP 422；目录创建最多 32 层。Vue 逐层请求，刷新取消旧请求，不缓存授权。

Host 角色成员替换最多接受 10000 个用户，参数化 JSON 集合读写代替逐用户往返。角色版本 CAS、完整新旧成员并集锁定、逐用户安全戳轮换及全部会话撤销在同一本地事务中完成；即使成员不变也执行既有撤销语义，失败 Result 或异常全部回滚。

## 2026-10-20 性能批次运行约束

Kafka 每个消费组使用专用后台线程调用同步 Consume，单 Worker 最多 64 个消费组；超过预算启动失败。各组独立启动，空闲组不会阻止后续组进入轮询。任一组故障会取消其他组，等待排空后向宿主传播异常，不能把轮询转移到普通线程池长期占用。

ImportExport 的 `FullNet:ImportExport:ExecutionTimeoutSeconds` 与 Reporting 的 `FullNet:Reporting:Export:ExecutionTimeoutSeconds` 默认 900 秒，允许 30–7200 秒。续租使用独立作用域，不延长单次执行截止时间；本地租约过期、续租失败或宿主取消均取消执行。终态写入在事务内重新核验当前时间、租户和租约，迟到结果不能覆盖新持有者。

Office 转换的 `Document:OfficePreviewConversion:MaxInputBytes` 与 `MaxOutputBytes` 默认各 50 MiB，按实际流量计算；`MaxTemporaryBytes` 默认 150 MiB，覆盖本地工作目录中的输入、输出和中间文件。总转换超时包含输入复制；取消或超预算会停止并回收转换进程。HTTP 转换响应先进入有界临时文件，不能仅信任 Content-Length。

匿名分享预览访问或首次创建消耗一次分享访问额度，随后使用绑定分享、文档、固定创建操作与原始截止时间的凭据轮询。重复创建返回同一任务，不延长截止时间；分享策略变化、撤销和文档回收仍即时拒绝。`Document:AnonymousPreviewPollRateLimitPermitLimitPerMinute` 默认 120，允许 1–600；页面每 5 秒轮询并遵守 Retry-After，访问和创建仍使用原有独立限流。

Identity 的组织隶属筛选只读取 Identity 自有投影；Organization Worker 以源版本发布 Outbox，投影仅用于目录筛选，不作为授权依据。部署需执行双库迁移 273–275，并启用 Organization 与 Identity 所需 Worker 能力；投影追平前筛选结果可能短暂滞后。

角色候选分页编辑通过 `CandidateUserIds` 声明本次已见候选集合，保存只替换该集合内的选择，保留未加载成员。角色入口和用户入口均先按数据库 UUID 顺序锁角色，再锁用户，并更新角色版本；陈旧候选编辑必须重新读取，不能覆盖并发赋权。

本批列表契约直接演进为分页摘要，正文、画布和模板内容按明细接口读取，所有客户端须同步生成并发布。Git 历史兼容检测仍会报告这种契约变化；不新增豁免或兼容层。性能收益尚无容量实测，状态保持 `Capacity-not-verified`。
