# 执行清单 51 — 首个非 S3 存储 Provider（OSS）与管理展示 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **51**（选定 **阿里云 OSS** 为首个非 S3 Provider；存储 Provider 目录与连通性测试 UI；**不**批量迁移历史文件）。

## 选定厂商

| 项 | 值 |
|----|-----|
| 非 S3 Provider | `oss`（`OssHostFileBlobStorage`） |
| 并存 | `local`、`s3` 仍注册；`FileStorageProviderRegistry.Resolve(providerKey)` **不回退**默认实现 |
| 未选 | 腾讯云 COS 未进入本槽 |
| 配置 | `Files:Storage:Oss` + 环境凭据（`ResolveCredentials`）；生产默认 Provider 可为 `s3` 或 `oss` |

## 交付锚点

| 层 | 位置 |
|----|------|
| OSS 实现 | `OssHostFileBlobStorage`；`OssFileStorageOptions` |
| 目录 API | `GET /api/v1/files/storage-providers`；`FileStorageProviderCatalogService`（脱敏 `ConfigurationSummary`） |
| 连通性 | `POST …/{providerKey}/test`（OSS/S3 受界探测；local 明示不支持） |
| Vue | `StorageProvidersView`（默认标记、配置状态、测试连接） |
| 补偿 | `PendingHostFileReconciliation` / `PendingTenantResourceFileReconciliation` 按行内 `ProviderKey` 解析 |
| 契约 | `files-storage-providers-v1` + `tests/openapi/files-storage-providers-contract.test.mjs` |
| 数据 | 迁移 047 `StorageProviderKey` 列（历史对象保留原 key） |

## 清单 51 验收结论

- **已有**：三 Provider 注册表；新上传走 `DefaultProviderKey`；旧 `local`/`s3` 对象仍可读；无批量重写工具。
- **本槽**：`phase-c-51-storage-providers.spec.mjs`（目录含 `oss`、摘要无密钥样式、local 测试 fail-soft、未知 key 404、管理页冒烟）。
- **未验**：真实 OSS Bucket 上的 put/get 与连通性探测成功（需阿里云账号；real-stack 未跑）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FileStorageProviderCatalog` / `Registry` / `OssHostFileBlobStorage` | **20/20**（`d40de4e6`） |
| `node --test` `files-storage-providers-contract.test.mjs` | **2/2** |
| real-stack | `phase-c-51-storage-providers.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
