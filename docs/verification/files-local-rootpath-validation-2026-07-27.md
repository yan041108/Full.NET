# Files 本地存储 RootPath 校验验证记录

- 日期：2026-07-27（Asia/Shanghai）
- 分支：`codex/files-rootpath-validation`
- 初始基线：`main@a3b8844ce9069ee9965e3a62594c7ef3a4ecaa7b`
- 最终同步基线：`main@aff0216648e463bfca940c0deebe11e8d6eb5869`
- 功能提交：`3b77b9dbafede48db416c15ce0b3d61acc15f36a`
- main 合入内容：`main@3b9c3c01c2a4caae9c67a0e4fa12486391c47318`
- 状态：实现、最终门禁、main 合并与隔离分支/工作树清理均已完成

## 范围与契约

本切片修复 `Files:Local:RootPath` 在 Windows 超长路径配置下的启动校验：

- `Path.GetFullPath` 抛出 `PathTooLongException` 时，Options Validator 返回配置失败；
- 失败沿用既有非敏感消息，不回显超长路径；
- 空白路径、非法字符、指向现有文件、非正上传上限和合法目录路径的既有语义保持不变；
- 不改变 Files API、数据库、Blob 对象键、上传大小契约、客户端或 canonical 测试数量。

## RED / GREEN

| 阶段 | 证据 |
| --- | --- |
| 基线 | 既有 `LocalFileStorageOptionsValidatorTests` **1/1** |
| RED | 同一测试方法加入 33,000 字符 Windows 路径后，按预期抛出 `PathTooLongException`，证明 validator 未把异常转换为配置失败 |
| GREEN | 最小加入 `PathTooLongException` 捕获后，同一聚焦用例 **1/1**，失败 0、跳过 0 |

## 验证

| 门禁 | 结果 |
| --- | --- |
| Release 全解决方案构建 | 0 warning / 0 error |
| Unit | **404/404**，失败 0、跳过 0 |
| Files 聚焦 | **1/1**，失败 0、跳过 0 |
| Files SQL Server/MySQL Integration | **2/2**，失败 0、跳过 0，**1m03s** |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| Naming | **23/23** |
| Governance | **11/11** |
| Skill 契约 | **52** 项通过 |
| workspace | 通过 |
| main 合并后复验 | Release 0 warning / 0 error；Unit **404/404**；Files **1/1**；Governance **11/11** |

## 规则与 Skills 复盘

- 规则：本次是已有“外部资源边界必须启动期校验并覆盖失败路径”要求下的单次异常类型遗漏，
  回归已直接加入现有 validator 用例；没有重复遗漏、规则歧义或高风险事故证据，本次不新增
  或修改规则。
- Skills：本切片只包含单一 Options Validator 异常边界，没有形成三个以上需要工程判断的
  高复用流程，也未暴露 `fullnet-module-delivery` 缺口，本次无 Skills 变化。

## 状态结论

本切片达到 `Build-verified`：超长 Windows 本地存储路径会在启动期返回可诊断且不回显路径
内容的配置失败；合法配置仍通过双库 Files API 用例。S3/OSS Provider、租户文件与孤立 Blob
自动清理不在本切片范围，Files 整体状态不提升为 `Verified`。
