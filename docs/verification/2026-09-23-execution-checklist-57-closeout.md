# 执行清单 57 — 国密 SM2 签验与密钥状态 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **57**（首个受控 **SM2** 集成载荷签名/验签 + 密钥状态目录；私钥仅 `FullNet:Cryptography:Sm2PrivateKeys` 部署注入；**无**匿名通用加解密 API）。

## 选定实现与用途

| 项 | 值 |
|----|-----|
| 算法 | **SM2**（`GmSm2SignatureEngine`，BouncyCastle 国密曲线） |
| 场景 | **integration payload** 签验（`SigningPurpose` / 种子键 `host-integration-signing`） |
| 私钥 | 配置字典注入；目录 API 仅 `PrivateKeyConfigured` 布尔，**不回显**私钥材料 |
| 未做 | SM4 通用加密、匿名 decrypt、HSM 生产接线（另编号/部署） |

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/cryptography`（`status` / `keys` / `sm2/sign` / `sm2/verify`） |
| 权限 | `cryptography.keys.read` / `cryptography.sm2.sign` / `cryptography.sm2.verify` |
| Vue | `CryptographyGmKeysView`（导航：**国密密钥**） |
| 数据 | 迁移 159 `fn_cryptography_key` 种子 |
| 契约 | `cryptography-gm-keys-v1.json`；`platform-backup-crypto-mqtt-observability-contract.test.mjs` §国密 |
| 单元 | `GmSm2SignatureEngineTests`（签验往返、开发种子稳定） |

## 清单 57 验收结论

- **已有**：仅 sign/verify 端点；退役键禁止签名；验签失败 `cryptography.sm2.signature_invalid`。
- **本槽**：`phase-c-57-cryptography-gm-keys.spec.mjs`（目录无私钥字段、伪造验签 422、签名配置缺失 409 或签验往返、页面冒烟）。
- **未验**：生产 HSM/合规国密库与真实私钥注入下的 sign（real-stack 常为空私钥配置）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`GmSm2SignatureEngineTests` | **2/2**（`d40de4e6`） |
| `node --test` `platform-backup-crypto-mqtt-observability-contract.test.mjs` | **4/4** |
| real-stack | `phase-c-57-cryptography-gm-keys.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
