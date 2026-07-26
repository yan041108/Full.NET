# OpenAPI schema 完整性守卫验证

## 范围

- 拒绝同一 schema 内重复的属性名，避免兼容比较转为集合时静默吞掉重复项。
- 拒绝不存在的 `requestSchema`、`responseSchema` 与 `itemSchema` 引用。
- 复用既有 breaking gate 测试方法，不增加 OpenAPI 或 breaking gate 测试发现数。
- 不修改 API、数据库、客户端、Architecture 或 canonical 门槛文件，不占用 Docker。

## 测试先行证据

1. 基线相对 `HEAD` 比较 25/25 通过。
2. 在 `SampleResponse.properties` 中重复加入 `id` 后，比较器仍返回状态 0；新增重复属性守卫后返回状态 1，并报告契约文件、schema 与属性名。
3. 新增一个引用不存在请求/响应 schema 的操作，以及引用不存在 `itemSchema` 的集合 schema 后，比较器仍返回状态 0；新增引用守卫后返回状态 1，并逐项报告引用位置与目标名。
4. 聚焦 breaking 测试文件保持 7/7，当前真实契约相对 `HEAD` 保持 25/25。

## 兼容边界

- 合法新增 path、operation、schema 与 schema property 继续允许。
- schema 引用只在同一契约文件内解析，符合当前离线夹具的自包含边界。
- 属性名按 JSON 字段的精确大小写比较，不把大小写不同的两个字段静默合并。
- 本守卫检查当前契约自身完整性；已有的删除与稳定字段变更比较语义保持不变。

## 验证结果

| 验证项 | 结果 |
| --- | --- |
| 聚焦 TDD 场景 | 2 轮 RED → GREEN |
| breaking gate 测试文件 | 7/7 通过 |
| 当前契约相对 `HEAD` | 25/25 通过 |
| OpenAPI 全量 | 58/58 通过 |
| Governance | 11/11 通过 |
| 项目 Skill 测试 | 52 项契约检查通过 |
| Workspace 校验 | 退出码 0 |
| `git diff --check` | 退出码 0 |

本切片保持 OpenAPI 58 与 breaking 25 的既有发现数，不改变 .NET canonical 门槛。

## 规则与 Skill 复盘

- 现有规则已经禁止静默改变公共 API，并要求兼容演进；本次补齐确定性校验，不新增近义规则。
- 本流程仍由 comparator、测试与 CI 自动执行，不需要人工工程判断；不创建或修改项目 Skill。
