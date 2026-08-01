# AWSSDK.S3 依赖评审（2026-08-01）

## 范围与基线

- 目的：为 Files 模块生产默认 `s3` Provider 引入 `AWSSDK.S3`，满足 ADR-0005 多实例共享对象存储基线。
- 代码基线：Task 10 实施窗口；中央包版本固定 `AWSSDK.S3 4.0.101.4`。
- 审查命令（本机新鲜输出）：
  - `dotnet list src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj package --include-transitive`
  - `dotnet list ... package --vulnerable --include-transitive`
  - `dotnet list ... package --outdated --include-transitive`

## 维护状态

- 包由 Amazon Web Services 官方维护（NuGet 发布者 `awsdotnet`），仓库 [aws/aws-sdk-net](https://github.com/aws/aws-sdk-net/) 仍活跃。
- `4.0.101.4` 为计划锁定版本；审查时 NuGet 已有补丁 `4.0.101.6` / Core `4.0.100.9`，属常规维护增量，本切片不自动追新，后续依赖升级窗口单独评估。

## 传递依赖树

| 包 | 解析版本 | 角色 |
| --- | --- | --- |
| AWSSDK.S3 | 4.0.101.4 | 直接依赖 |
| AWSSDK.Core | 4.0.100.8 | S3 传递依赖 |

未引入其他业务无关 AWS 服务包。Files 模块通过窄接口 `IS3BlobClient` 隔离 SDK，测试可注入内存替身。

## 许可证与再分发

- 许可证：Apache-2.0（与仓库既有 Dapper/Serilog/OpenTelemetry 同类）。
- 与 Full.NET MIT 再分发兼容；已同步 `THIRD-PARTY-NOTICES`。
- NOTICE/版权归属仍以 NuGet 包内与上游仓库为准。

## 安全审计

- `--vulnerable --include-transitive`：在当前 NuGet 源下，项目报告“没有易受攻击的包”。
- 凭据边界：AccessKey/SecretKey/SessionToken **不得**写入普通 appsettings；运行时仅读取 `Files__S3__*` / `AWS_*` 环境变量或工作负载身份相关环境变量。Production 或默认 Provider=`s3` 时缺凭据线索则启动失败。

## 包体积与裁剪 / AOT

- 本机还原缓存中 net8.0 程序集约：`AWSSDK.S3.dll` ~1.1MB、`AWSSDK.Core.dll` ~1.0MB；完整包含多 TFM 约 30MB。
- Full.NET 1.0 API/Worker 当前不为 Native AOT 发布门禁；SDK 反射与动态依赖意味着若未来启用 AOT，需单独做裁剪清单与替换评估。
- 备选退出：若体积或 AOT 成为硬门禁，可替换为 `Minio` 官方 .NET 客户端或自研 HttpClient SigV4 窄适配，同时保留 `IFileStorageProvider` 与 `ProviderKey=s3` 契约。

## 凭据与端点行为

- `EndpointMode=Aws`：要求 Region；使用区域端点，不伪造 ServiceUrl。
- `EndpointMode=Custom`：要求 HTTPS（或非 Production 显式 `AllowInsecureServiceUrl`）、签名 Region、`ForcePathStyle=true`（MinIO 等兼容端点）。
- 客户端懒创建：未配置 Bucket/未触达 S3 路径时不强制构造 AmazonS3Client（开发默认 `local`）。

## 验证结论

- Files Unit（含 S3 Provider/校验/注册表）：通过（60 条聚焦用例）。
- 跨实例共享命名空间：Integration 使用内存 `IS3BlobClient` 替身验证 A 写 B 读删；**真实 MinIO/S3 容器未在本机启动，不得声称容器 Integration 通过**。
- 依赖评审门槛：维护状态、依赖树、许可证、漏洞扫描、体积/AOT 备注、凭据边界与退出条件均已记录；允许合入 Task 10 Provider。

## 未验证项

- 生产等价 MinIO/AWS S3 的超时、重试、跨区域与故障注入。
- Native AOT / 链接器完整裁剪清单。
- 容量与多副本对象存储 SLA（仍标记 `Capacity-not-verified`）。
