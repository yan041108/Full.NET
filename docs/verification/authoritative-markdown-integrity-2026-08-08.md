# 权威 Markdown UTF-8 完整性验证记录

- 日期：2026-08-08
- 快照：`architecture-doc-integrity-20260808`
- 基线提交：`729a7a61`

## 范围

扫描器覆盖 `AGENTS.md`、`rules/`、`.agents/skills/`、`docs/architecture/`、`docs/roadmap/`、`docs/superpowers/specs/`、`docs/superpowers/plans/` 与 `docs/operations/` 下的全部 `.md` 文件。

## 门禁规则

1. 字节级 `TextDecoder('utf-8', { fatal: true })` 解码。
2. 拒绝 UTF-16 BOM、无效 UTF-8、`U+FFFD`。
3. 排除 fenced code、行内 code 与 URL/链接目标后，拒绝正文连续 ASCII `???` 乱码串；保留中文问号 `？` 与代码中的合法 `?`。

## 验证命令与结果

```powershell
node --test tests/governance/authoritative-markdown-encoding.test.mjs
pnpm test:governance
```

| 命令 | 结果 |
| --- | --- |
| `authoritative-markdown-encoding.test.mjs` | 9/9 通过 |
| `pnpm test:governance` | 26/26 通过 |

实现文件：`scripts/governance/validate-authoritative-markdown.mjs`。

## 说明

- 扫描器只报告路径与首个违规位置，不自动修复文件。
- 无路径 allowlist；临时 fixture 通过完整权威根目录树验证拒绝逻辑。