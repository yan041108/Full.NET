# 日志秘密跨出口与实例身份回归

代码基线为 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加本任务工作区变更。运行环境为 Windows / Docker Desktop，TLS Kafka 4.1.2、固定 Fluent Bit 4.1.1 与固定 HTTPS Elasticsearch 测试夹具。专项命令为 `pnpm test:logging:secret-boundary:live`，两条入口各发送三条诊断日志，不进行容量压测。

原始日志包含十个随机秘密哨兵、原 IP 和服务器地址，覆盖 Password/Token/Cookie、RequestBody/ResponseBody、嵌套 ClientSecret/ResponseBody、Bearer 文本、异常消息和 `Authorization: {Value}` 的不透明值。哨兵不保存到原始证据或错误消息；检查器对每个哨兵做拒绝自验证。来源必须非空且恰为三条，安全标记和异常类型必须保留。

Collector 读取正式 Console JSON 的测试文件副本，经正式 CRI/可信元数据/过滤链与 TLS Kafka 出口；ApplicationKafka 在正式 Producer 前复制三个 owned 安全快照并实际发送，Console 必须为空。Broker 总水位要求 General=2、Priority=1，逐 ID/字段核对来源；Collector 六条原始/镜像输入只准三个原始 ID 到达 Broker。正式独立消费者以私有 CA 与受限 API Key 写 HTTPS ES，完整 `_source` 与安全 Broker 内容一致，最终 Offset 等于高位点，DLQ=0。资源清理与消费者/采集器诊断字节检查完成后才写每路线 `artifacts/logging-secret-boundary/<route>/result.json`。

首次运行发现实际交付缺陷：凭据模板安全替换后，`AddRestrictedMetadata` 没有保留 `Instance`，Collector 的必填字段过滤使该条日志丢弃（tail=6，出口=2）。新增单测先实际失败于缺少 Instance。修复仅为受限元数据增加 Instance，并限制为 36 字符、D 格式 UUID；生产 Enricher 仍使用进程启动 UUID 强制覆盖，不允许业务调用方伪造。新负例拒绝任意实例字符串及含凭据字符串，其他秘密属性与模板值仍遵循既有脱敏规则。

## 最终验证

日志单测 `pnpm test:dotnet:unit -- --selection logging-delivery` 已通过 318/318、0 跳过，构建 0 警告/错误。修正版专项 `pnpm test:logging:secret-boundary:live` 已通过 2/2、0 跳过，耗时 2 分 34 秒；两路线分别三条 ES 文档，General committed=2、Priority=1，均等于最终高位点；DLQ=0、12 项秘密/地址哨兵检查、来源/文档与诊断检查、资源清理全部通过。初次 Collector 4 分钟超时不计为通过，修复后完整重跑。

`pnpm test:aot:publish:linux` 新鲜发布成功：Linux SDK 10.0.400、linux-x64 原生产物 133360528 字节、耗时 1040774ms，17 条既有准入第三方告警通过门禁。随后在该 SDK 容器中挂载仓库与 Docker socket，使用宿主网络和 `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`，直接运行集成 DLL 筛选 `NativeApiKafkaLogMySqlE2ETests`：1/1、0 跳过，耗时 3 分 50 秒。该用例实际启动新原生 Host.Api，核对普通/优先 Topic、Key/LogEventId、HTTP 404 优先操作记录及宿主正常退出。它覆盖 Host.Api 原生日志运行回归，不等于原生秘密哨兵矩阵；本轮未重跑 Worker/Migrator 原生闭包。

影响选择已补齐：安全测试使用独立分片；ES/消费者/采集器共享夹具补选请求、比较及安全分片；实时 CRI 桥接补选比较分片；日志 TLS 夹具选择 logging-kafka。相应选择测试先失败后通过，工具测试 54/54；集成发现共 1106 项，无遗漏或重复。发现不是全量测试通过。

`pnpm test:aot:analyzers`：0 警告/错误；`pnpm test:dotnet:architecture -- --selection api-native-aot`：73/73、0 跳过。集成 DLL 筛选 `Login_and_current_user_follow_secure_http_contract`：SQL Server/MySQL 两项 2/2、0 跳过。独立复核确认 UUID 限定未放宽原秘密规则，指出并修正共享夹具漏选。

治理 `pnpm test:governance`：57/57；`git -c core.safecrlf=false diff --check` 通过，新增文件单独执行无空白问题的 no-index 检查。分支仍为 `codex/foundation-acceptance-20260926`，既有无关工作区修改保留，本轮未提交代码。

此验证不覆盖独立 File Sink、旧 ES Sink、归档、B1 数据库、所有可能的秘密格式、DLQ 非空内容或容量。Console 落测试文件不等于文件出口能力验收；Development 临时 CA NoCheck 不改变 Production 吊销策略。
