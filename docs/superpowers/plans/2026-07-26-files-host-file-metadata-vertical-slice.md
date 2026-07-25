# Files Host 文件元数据纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。新建 `Full.NET.Modules.Files` 主项目；Contracts 放主项目内 `Contracts/`，禁止再拆 `.Contracts` / `.Http`。

- 建立日期：2026-07-26
- 状态：**Build-verified**
- 验证：[验证记录](../../verification/files-host-file-metadata-2026-07-26.md)
- 批准依据：
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「文件与对象存储」
  - [总体架构 §6.6](../specs/2026-07-17-fullnet-architecture-design.md#66-files)
  - 在线用户切片完成后，Files 为 Admin.NET 对标矩阵下一项 Core/M3 能力

**Goal:** Host 管理员上传、分页列表、下载与软删除文件；默认本地磁盘存储 Provider；双端管理 UI 与 Mock parity。

**Architecture:** 新模块 `Files`；表 `fn_files_file`；权限 `files.files.read` / `files.files.write`；API 前缀 `/api/v1/files`；导航 `files` → `/files/host-files`。存储路径由配置 `Files:Local:RootPath` 提供，对象键由服务端生成，禁止客户端直写路径。

**Tech Stack:** DbUp `027` 双库迁移、Dapper、multipart 上传、ProblemDetails、Vue/Layui、Playwright。

---

## 范围与非目标

### 必须交付

1. 双库迁移 `027_FilesFile.sql`（常规聚集主键；`TenantId` 可空表示 Host 作用域）。
2. Host 分页列表、按 Id 元数据、multipart 上传、流式下载、软删除。
3. 本地文件系统 Provider（写入、读取、删除物理文件与元数据一致）。
4. OpenAPI + Integration 双库 + 双端 UI + Mock parity。

### 非目标

- S3/MinIO/OSS/COS Provider、分片上传、断点续传、病毒扫描。
- 租户作用域文件、临时文件清理任务、富文本资源标记解析。
- 图片缩略图、预览、公开匿名下载。
- 标记 `Verified`。

---

## 附录 A：数据模型

### `fn_files_file`

| 列 | 说明 |
|---|---|
| Id | UUID v7 |
| TenantId | Guid?，Host 切片固定 null |
| OriginalFileName | nvarchar(260) |
| ContentType | varchar(128) |
| SizeBytes | bigint |
| StorageKey | varchar(512)，相对 RootPath 的对象键 |
| ContentHash | char(64)?，SHA-256 hex |
| CreatedAtUtc | datetimeoffset |
| CreatedByUserId | Guid |
| DeletedAtUtc | datetimeoffset?，软删除 |

---

## 附录 B：API

| 场景 | 方法 | 路径 | 权限 |
|---|---|---|---|
| 列表 | GET | `/api/v1/files/host-files` | `files.files.read` |
| 元数据 | GET | `/api/v1/files/host-files/{id}` | 同上 |
| 上传 | POST | `/api/v1/files/host-files` | `files.files.write` |
| 下载 | GET | `/api/v1/files/host-files/{id}/content` | `files.files.read` |
| 删除 | POST | `/api/v1/files/host-files/{id}/delete` | `files.files.write` |

---

## 任务分解

### Task 1: 模块骨架、迁移与 RED

1. [x] 本计划。
2. [x] `Full.NET.Modules.Files` + Composition 注册。
3. [x] `027` 双库迁移；权限/导航。
4. [x] RED：无权限 403；Integration **154 → 156**。

### Task 2: 存储 Provider + HTTP API

1. [x] `IHostFileBlobStorage` + 本地实现；上传/下载/删除与事务内元数据写入。
2. [x] Query/Management Endpoint + OpenAPI 夹具。

### Task 3: 双端 UI 与 E2E

1. [x] Vue/Layui 列表 + 上传 + 下载 + 删除。
2. [x] `shell-parity`「Host 文件列表与上传删除」× 双端 → **54 → 56**。

### Task 4: 文档与门槛

1. [x] 验证记录、`capability-status`、四处 canonical 门槛。

### Task 5: 真实栈 E2E

1. [x] `host-files.spec.mjs`：API 上传 + 双端列表加载；受限账号 403 与导航裁剪。
2. [x] `uploadHostFileViaApi` 辅助函数；真实栈门槛 **76 → 80**。
