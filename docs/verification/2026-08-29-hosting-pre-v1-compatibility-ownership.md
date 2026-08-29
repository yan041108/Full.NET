# Hosting pre-v1 兼容映射归属验证

## 范围与基线

- 基线提交：`6a981837`
- 日期：2026-08-29
- 范围：pre-v1 error_code 映射的程序集归属，以及标准错误资源本地化边界。
- 非目标：不修改 canonical/legacy 映射值、HTTP 状态码、Admin.NET 包络、模块资源文本或客户端兼容逻辑。

## 变更

- `PreV1ProtocolCompatibility` 从通用 `Full.NET.Hosting` 迁入 `Full.NET.Compatibility.AdminNet`。
- `ResourceErrorMessageLocalizer` 只按 canonical error_code 查询模块资源，不再知道 Identity/Tenancy 的 legacy 别名。
- Admin.NET 兼容包络继续在显式 `EmitLegacyErrorCodes=true` 时把 canonical code 映射回既有 legacy code，默认仍输出 canonical。
- 映射测试迁入 Compatibility 测试项目；Architecture Test 固定 Hosting 不得重新拥有该业务兼容表。

## 兼容性说明

标准 API 行为不变：生产错误码和模块资源均已使用 canonical 值。Admin.NET 兼容行为不变。该切片只收窄 pre-v1 公共类型的程序集与命名空间；仓库外若直接调用原 `Full.NET.Hosting.Api.PreV1ProtocolCompatibility`，需迁移到 `Full.NET.Compatibility.AdminNet.PreV1ProtocolCompatibility`。

## 验证结果

- TDD：`PreV1_error_code_map_is_owned_by_compatibility_adapter` 先因 Hosting 仍拥有类型而失败，迁移后通过。
- Compatibility：12/12 通过，覆盖映射、未知码透传、模块 canonical 目录与 Admin.NET 包络。
- Hosting 本地化与标准结果映射 Unit：18/18 通过。
- pre-v1 生产源码命名门禁：1/1 通过；精确例外已从旧 Hosting 路径迁到 Compatibility 路径。
- `pnpm test:aot:analyzers`：0 警告、0 错误。
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：73/73 通过。
- `pnpm test:inner -- --snapshot hosting-pre-v1-compatibility-ownership`：Release 构建 0 警告、0 错误；MySQL smoke 4/4 通过。
- `pnpm test:aot:publish:linux`：Docker Linux SDK 完成 API `linux-x64` Native AOT 链接，warning gate 接受 9 个既有精确告警；原生可执行文件 72,114,192 bytes。
- `pnpm test:governance`：52/52 通过。

完整 `pnpm test:naming` 的本切片相关 pre-v1 门禁已通过；套件仍被其他窗口新增的 `100_MessagingDomainAuditRequestedOutcome.sql` 四处 `FNSQL003` 阻断，本切片未修改或吸收该迁移。

## 演进检查

本切片落实既有 BuildingBlocks 不持有业务模块知识的边界，并增加可执行架构门禁；没有出现新的通用规则或 Skill 缺口。
