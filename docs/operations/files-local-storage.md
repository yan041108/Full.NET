# Files 本地存储运维说明

## 配置

本地 Provider 使用以下配置：

```text
Files__Storage__DefaultProviderKey=local
Files__Local__RootPath=<持久化绝对路径或宿主工作目录下的受控相对路径>
Files__Local__MaxUploadBytes=10485760
Files__Cleanup__Enabled=false
Files__Cleanup__BatchSize=100
Files__Cleanup__MaxBatchesPerRun=10
Files__Cleanup__PollSeconds=300
```

`DefaultProviderKey` 必须是已注册的小写稳定机器码；`RootPath` 不得为空，`MaxUploadBytes`
必须大于零。API Host 使用 `ValidateOnStart`，Provider 重复、默认 Provider 未注册或配置无效时拒绝
启动，不会延迟到首个上传请求。

迁移 `047_FilesStorageProvider` 会把存量对象归属回填为 `local`。新上传使用当时配置的默认
Provider，并把 `ProviderKey` 与 `StorageKey` 一起持久化；下载、删除、补偿和后台墓碑清理始终按
记录中的 Provider 路由。切换默认配置只影响新对象，未知或不规范的存量机器码会失败关闭，禁止
回退到当前默认 Provider。

Development 默认使用 `App_Data/files`。Production 应显式配置挂载到持久卷的目录，不应依赖容器临时文件系统或可随发布覆盖的应用目录。

软删除 Blob 清理只由 Worker Profile 承载且默认关闭。只有 Worker 与 API 能访问同一持久化
`RootPath` 时才可启用；启用但未配置有效根目录会拒绝 Worker 启动。每轮最多处理
`BatchSize * MaxBatchesPerRun` 个墓碑，三个数值的允许范围依次为 `1..1000`、`1..100` 和
`5..86400` 秒。

## 权限与安全

- 运行账号只授予该根目录所需的读取、创建、替换和删除权限，不授予父目录或系统目录写权限。
- 文件名不参与物理路径拼接；存储键由服务端 UUID v7 与年月目录生成。
- 上传先在最终对象同目录写入唯一 `.uploading` 暂存文件，完整复制并刷新后才以不覆盖方式移动到
  最终路径；上传取消、复制失败或目标键冲突时不会发布部分最终对象，并会清理本次暂存文件。
- 反向代理和 API Host 的请求体大小限制不得低于 `MaxUploadBytes`，也不得无界放大。
- 根目录不得由 Web Server 直接静态暴露，下载必须经过鉴权 API。
- Provider key 不得来自上传请求、CLR 类型名或文件名；外部 Provider 只能通过受信任 DI 注册和宿主配置接入。

## 备份与恢复

- 元数据表 `fn_files_file` 与文件根目录必须纳入同一恢复点策略；恢复演练需核对元数据、大小和 SHA-256。
- 数据库备份与文件快照无法原子完成时，应记录时间窗并以内容哈希生成差异清单。
- 软删除提交后才尽力删除物理文件，因此删除失败可能留下孤立 Blob，但不会出现“数据库回滚、文件已永久删除”。
- 后台清理对单个 Blob 先执行幂等物理删除，再精确删除仍处于软删除状态的数据库墓碑；物理删除
 失败会保留墓碑供下一周期重试，数据库删除失败也不会被吞掉；清理不会尝试当前默认 Provider
  之外的替代实现。

## 孤立文件清理

启用 `Files:Cleanup` 后，Worker 会按 `DeletedAtUtc + Id` 顺序小批量处理数据库已知的 Host
软删除墓碑。单个不可删除对象不会阻塞同轮后续候选；Blob 已不存在或墓碑已被另一实例清除均按
幂等完成处理。

该任务不扫描文件系统，因此无数据库墓碑的历史孤立 Blob，以及文件系统拒绝删除的暂存文件，
仍只能在只读清单确认后处理：

1. 从数据库导出仍有效的 `StorageKey` 集合。
2. 扫描根目录并生成差异清单，不直接删除。
3. 对超过保留期且不在有效集合中的对象做备份或移入隔离区。
4. 复核审计记录、文件数量和容量后再清理隔离区。

禁止用递归删除命令直接清空根目录。

当前发布物只包含 `local` Provider。S3、OSS、MinIO 等外部实现必须单独完成依赖与许可证审查、
启动期配置校验和 Provider 专属集成环境，不得仅靠实现接口宣称可用。
