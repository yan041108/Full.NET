# 客户端工具链 braces 安全修复包

本包是 Full.NET 私有工具链包 `@fullnet/braces@0.1.0`，由根 pnpm override 替换传递依赖 `braces`。它不是上游发布的修复版本，也不作为业务客户端 API。

上游来源为 MIT 许可的 [braces 3.0.3](https://github.com/micromatch/braces/tree/3.0.3)，提交 `74b2db2938fad48a2ea54a9c8bf27a37a62c350d`。保留原 LICENSE、作者代码及公开 API；npm 官方 tarball 的 SHA-512 完整性记录于 package.json 的 `fullnetUpstream`。源码已与该 tarball 比对，新增 limits.js，修改 parse/compile/expand/stringify 四个入口，其余上游实现未变。

[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm) 在本批核查时没有上游修复版本。漏洞源于深层 AST 的无界递归；既有字符串长度限制不能保护外部 AST 或字符预算内的深层模式。

- 解析器在圆括号、花括号入栈前固定限制深度 64，闭合和未闭合模式均受约束。
- 三个 AST 消费入口先迭代验证子节点深度，再进入原递归算法；节点必须组成严格树、子列表必须是 Array、value 必须是字符串，拒绝子节点环、共享子树、非数组迭代器和循环数组值。expand 的 parent 必须对应当前树祖先，不能引入外部队列。
- 超限抛出带 `ERR_BRACES_DEPTH_LIMIT` 的 SyntaxError；不合法 AST 抛出带 `ERR_BRACES_INVALID_AST` 的 TypeError。调用选项不能提高该上限；原字符数、范围展开和转义规则继续保留。
- Node 回归从实际 DCloud/chokidar 依赖链加载本包，覆盖恶意字符串、深层及循环 AST、解析回退、正常替代/范围/转义和不能放宽的上限；uni-app 三目标及 H5 浏览器验证覆盖真实消费路径。

安全修复没有通过审计例外接受旧漏洞；根策略 exceptions 为空。以后上游推出已修复版本时，核查变更并通过这些回归后，可移除 override 和本地修复包。本地分支后续也必须持续关注上游新公告，不能仅依赖 npm 对包名的识别。
