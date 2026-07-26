# Files Host 文件元数据验证记录（2026-07-26）

- 范围：Host 文件上传、列表、下载、软删除；本地磁盘存储；Vue/Layui 管理页
- 计划：[实施计划](../superpowers/plans/2026-07-26-files-host-file-metadata-vertical-slice.md)
- 状态：**Build-verified**

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | `Host_file_management` SQL Server/MySQL **2/2** → **154 → 156** |
| OpenAPI 夹具 | `files-host-files-v1` 静态 **2/2** |
| client-contracts | `host-files` **1/1** |
| Vue API 单测 | `host-files.test.ts` **3/3** |
| Layui 单测 | `host-files.test.js` **1/1** |
| Mock parity | 「Host 文件列表与上传删除」× 双端 **2/2** → `shell-parity` **54 → 56** |
| 四处 canonical 门槛 | **359/7/40/172** |

## 行为摘要

- 元数据表 `fn_files_file`；`TenantId` 为空表示 Host 作用域
- 上传 multipart 字段 `file`；存储键 `host/{yyyy}/{MM}/{id}`
- 删除先提交元数据软删除，再尽力删除物理文件；失败时保留可恢复的孤立 Blob，后续由清理任务处理
- `Files:Local:RootPath` 与 `MaxUploadBytes` 在宿主启动时校验，Production 缺失配置会 fail-fast
- Production 路径、权限、备份与孤立文件清理见[本地存储运维说明](../operations/files-local-storage.md)

## 增补（2026-07-26，真实栈 E2E 脚本）

| 层 | 结果 |
|---|---|
| 脚本 | `tests/e2e/admin-real-stack/tests/host-files.spec.mjs`（2 场景 × 双端） |
| 真实栈门槛 | **76 → 80** |
| 新鲜实跑 | 本机无 Testcontainers 时未重跑；以 CI `real-stack-e2e` / `real-stack-e2e-mysql` 为准 |

## 非目标

- S3/OSS Provider、分片上传、租户作用域文件
