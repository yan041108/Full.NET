# Files 存储 Provider 边界验证（2026-08-01）

## 交付边界

- `IFileStorageProvider` 统一流式保存、读取与幂等删除契约，`ProviderKey` 使用规范小写稳定机器码。
- `FileStorageProviderRegistry` 在重复 key、未知默认值、未知存量值和非规范 key 时失败关闭。
- 新上传按受信任的 `Files:Storage:DefaultProviderKey` 选择 Provider，并持久化 `ProviderKey`。
- 下载、软删除后补偿和 Worker 墓碑清理按记录中的 Provider 路由；切换默认值不改变既有对象归属。
- `047_FilesStorageProvider` 为 SQL Server/MySQL 回填 `local`，收紧非空列，并建立
  `(ProviderKey, StorageKey)` 唯一索引；半完成迁移可重跑恢复。
- 保留本地 Provider 的同目录暂存、完整刷新后原子发布、取消清理、路径越界拒绝和幂等删除。

本切片未引入 S3、OSS、MinIO 包、独立 Contracts 项目或客户端可选 Provider 参数。

## TDD 与验证证据

- 首个 RED：Provider 接口和注册表类型缺失，聚焦 Unit 编译以 `CS0246` 失败。
- Registry、管理服务、清理任务和本地存储聚焦 Unit：通过。
- SQL Server/MySQL 047 半完成恢复测试：通过。
- `pnpm test:naming`：通过。
- Integration Release build：0 warning / 0 error。
- task snapshot slice：CodeGeneration、Files、migration-047、Realtime 组合影响集通过；矩阵工具、分片与治理门禁通过。
- fresh discovery 已同步到 `eng/testing/test-matrix.json`；该文件仍是唯一测试门槛事实源。

## 独立审查与后续风险

- 审查发现 SQL Server 初稿使用活动行过滤唯一索引，而 MySQL 对全表唯一。该差异会允许墓碑与活动
  元数据复用同一对象键，后台清理存在误删风险；最终实现已统一为全表
  `(ProviderKey, StorageKey)` 唯一，并由恢复测试验证唯一性、无过滤和存量 `local` 回填。
- 既有上传流程无法区分“数据库确定回滚”与“提交已生效但客户端未收到结果”。在后一场景无条件
  Blob 补偿可能造成活动元数据断链。`ICommandTransaction` 当前不提供提交确定性，Task 6 不采用
  二次查询或吞异常的局部修补；该风险已移交 Files 后续独立状态机切片，要求 fresh snapshot、计划
  和 TDD 设计 Pending/Ready 或等价可对账协议。
- 上传上限仍来自 `Files:Local:MaxUploadBytes`。首个外部 Provider 接入时必须把通用上传策略与
  Provider 专属配置拆开，并为各 Provider 提供启动期验证。

## 运行结论

Files 已具备接入第二个真实 Provider 所需的核心路由边界，但当前只有 `local` 实现。外部 Provider
在完成依赖与许可证审查、启动配置验证、真实对象存储环境、失败恢复和运维手册前不得标记为可用。
## 后续风险关闭（2026-08-01）

上述“提交已生效但客户端未收到结果时补偿删除 Blob”的风险，已由独立 `pending` → `ready` 上传状态机与 Worker 对账切片关闭。上传不再依据事务异常删除 Blob；`048_FilesUploadState`、双库半完成恢复、提交不确定性 Unit 与 Files 双库真实栈证据见 `files-upload-commit-reconciliation-2026-08-01.md`。Task 6 当时拒绝二次查询/吞异常局部补丁的审查结论仍保留为决策历史。
