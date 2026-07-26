# OpenAPI 契约路由键唯一性验证

## 范围

- 校验每个结构化离线契约中的 path 唯一。
- 校验同一路径内的 HTTP method 唯一，并按大写规范化后比较。
- 复用既有 breaking gate 测试方法，不增加 OpenAPI 测试总数。
- 不修改 API、数据库、客户端、Architecture 或 canonical 门槛文件。

## 测试先行证据

1. breaking gate 基线相对 `HEAD` 为 25/25。
2. 加入重复 `/api/v1/samples` path 后，比较器仍返回状态 0，证明 path 会被 `Map` 静默覆盖。
3. 增加 path 唯一性扫描后，该场景返回状态 1，并输出契约文件与重复 path。
4. 随后加入重复 `GET /api/v1/samples`，比较器再次返回状态 0。
5. 增加 method 唯一性扫描后，该场景返回状态 1，并输出契约文件、规范化 method 与 path。

## 验证结果

| 验证项 | 结果 |
| --- | --- |
| 聚焦 TDD 场景 | 1/1 通过 |
| breaking gate 测试文件 | 7/7 通过 |
| OpenAPI | 58/58 通过 |
| 当前契约相对 `HEAD` | 25/25 通过 |
| Governance | 11/11 通过 |
| 项目 Skill 测试 | 52 项契约检查通过 |
| Workspace 校验 | 通过 |
| `git diff --check` | 通过 |

本变更不增加 OpenAPI 或 breaking gate 测试数量。
