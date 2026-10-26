# VForm 3 完整源码迁入、隔离与无损设计资产验证

日期：2026-10-24。入口基线：`main@a77902afd87af1662af3b43206d85160eeec566b` 加本轮工作区差异；任务快照 `vform3-full-source-20261024`。入口已有三份方案文档修改予以保留。本轮没有提交、推送或全量测试。

结论：固定来源完整源码、隔离设计器/预览与无损保存回显已实施。独立只读审查发现的时间精度、挂载初值问题已修复并复审，客户端 JSON 对象边界同步补齐。协调冻结后的当前输入已完成 Linux Native AOT 发布、VForm 原生双库 2/2、托管双库 2/2、严格 CSP 真实栈 1/1，以及定向管理端 104/104、Todo 契约 27/27 与生产构建，最终摘要及原始证据见末节。其它原生路径未扩测，整个 Workflow 不标为 Verified。活动要求见[实施计划](../superpowers/plans/2026-10-23-vform3-full-source-migration.md)及[规格 §6](../superpowers/specs/2026-08-20-workflow-module-design.md#6-表单引擎与跨端渲染)。

## 来源与变更

- `vform666/variant-form3-vite@c67479e496bab56a93a3dff168a4f529d8293c67`，338 文件、2,130,905 字节原样保留于 `packages/admin-form-designer/vendor/vform3/upstream/`，逐文件字节/SHA-256 见 [manifest](../../packages/admin-form-designer/vendor/vform3/manifest.json)。Git 属性关闭这批原始资源的行尾转换，防止 Windows autocrlf 破坏摘要。
- 原许可为 Variant Form 许可条款 1.0，并非 MIT。作者头、完整许可及打包依赖 Notices 随源码/产物保留；最小适配见 [PROVENANCE](../../packages/admin-form-designer/vendor/vform3/PROVENANCE.md)。没有修改上游目录内文件。
- 上游自身 `.gitignore` 的 `dist` 规则会忽略随源码提供的 6 个 VueDraggable 文件，根目录例外无法覆盖子目录规则。为保持原文件不变，只对这 6 个确切来源文件执行 `git add -f`；Git 可见清单 338/338，暂存字节摘要与原清单一致，未执行提交。
- 官方模板外部资源 16 文件、665,144 字节单独本地化和记录 URL/摘要，见 [external-assets](../../packages/admin-form-designer/vendor/vform3/external-assets.json)。模板 5 引用社区提交没有提供的 `sub-form`；它不是已迁入组件，不以这份外部示例冒充社区版能力。
- 删除旧精简 ESM 面板/目录与 VForm → 简化 Schema → 回显转换；Workflow 使用独立完整设计资产 GET/PUT，保存修订 CAS 与资产同事务，发布保留完整快照，业务 Schema 只作发布投影。新增双库迁移 286 和显式 AOT 物化器/JSON 元数据。

## 原版覆盖事实

以下清单以源码配置和扩展注册为事实源，不以现有业务字段目录裁剪。

| 范围 | 来源文件（相对 upstream/src） | 实施与定向结果 |
| --- | --- | --- |
| 18 基础字段 | `components/form-designer/widget-panel/widgetsConfig.js` | input、textarea、number、radio、checkbox、select、time、time-range、date、date-range、switch、rate、color、slider、static-text、html-text、button、divider；完整 JSON 往返及原版预览 |
| 4 高级字段 | 同上 | picture-upload、file-upload、rich-editor、cascader；真实预览；文件上传以受控测试回执验证 |
| alert 扩展 | `extension/extension-loader.js`、`extension/samples/extension-schema.js` | 原版注册、设计/预览与生成 SFC；补测包含 alert 的全部 23 字段通过 |
| 4 可添加容器 | widgetsConfig、extension-loader | grid、table、tab、card；内部 grid-col/table-cell/tab-pane 保留；官方模板 2/3/4/8 结构往返及真实预览 |
| 全部属性源码 | `components/form-designer/setting-panel/` 与扩展注册 | 完整复制并构建；23 字段逐项属性编辑并核对其余默认配置不变，复杂默认值、label/value、脚本、样式、根/组件扩展配置往返；未穷举所有属性组合 |
| 原版 Renderer | `components/form-render/` | 实际输入、只读、数据读取及变更回传；正式宿主复用隔离边界，最终真实页面填写/审批请求/数据库回读 1/1 |
| 原版工具栏 | `components/form-designer/toolbar-panel/`、widget-panel | 撤销/重做、PC/Pad/H5、组件树、清空、JSON 导入/导出及实际文件下载、本地模板、预览禁用/恢复/重置/获取数据、代码导出与 Vue 3 SFC 编译通过 |

全部 338 文件清单同时涵盖原版样式、图标、语言、属性编辑器、工具栏、扩展及渲染组件。适配位于来源目录之外：旧 eager glob、VueDraggable/当前 Vue、Ace 本地资源、Quill 2 值事件桥接、SFC HTML 绑定转义、初始化校验时机、导入配置清理和拖放提示层。

## 安全与恢复边界

- 设计、预览及 Web Renderer 使用 `sandbox="allow-scripts allow-downloads"`，不授予同源权限；父文档 `frame-src 'self'` 限制自导航，帧内 CSP 限定专用静态/演示路径。无宿主 Cookie/Token/DOM/存储访问，消息只允许固定读写/校验方法，核对窗口、Origin、随机通道、请求 ID 和大小。
- 原版脚本/事件仍在帧中执行，服务端只验证有界 JSON，不执行设计脚本。帧内 localStorage 使用内存实现；最终持久化走真实设计资产 API。Axios 禁止凭据与默认 XSRF Cookie 读取，避免沙箱模板加载触发 SecurityError。
- 业务投影不支持的控件/值形态拒绝发布并返回配置路径，保留原设计与上一活动版本。金额/精确小数和数字/布尔选项值不降级为文本或浮点发布。
- 待办渲染移除未授权输入、默认值及根设计扩展，隐藏字段与脚本/计算依赖并存时拒绝投影；Patch 按字段策略筛选，服务端仍是授权与业务校验权威。不以 FormVersionId 共用办理授权投影缓存。
- 双库场景在测试专属新数据库模拟：第一张资产表存在、第二张快照表缺失、286 尚未记账，重新迁移恢复成功。没有删除共享容器或其它任务数据库。
- 依赖审计：Vite/Vitest 更新至 8.1.6/4.1.12；`pnpm audit:clients` 通过精确政策。Quill 2.0.3 的 Low [GHSA-v3m3-f69x-jf25](https://github.com/advisories/GHSA-v3m3-f69x-jf25) 尚无上游修复，HTML 输入/变更/semantic 导出均经 DOMPurify，恶意事件属性 DOM/回传回归通过；仅此包路径的限期例外见 [policy](../../security/client-audit-policy.json)，复核日 2026-11-07、到期 2026-11-23。不能表述为零公告或上游漏洞已修复。

## 实际命令与结果

环境：Windows 10 Pro 10.0.19045、i7-12700H（20 逻辑处理器）、约 64 GiB 内存；Node 24.12.0、pnpm 10.26.0、.NET SDK 10.0.402、系统 Edge。构建通过项目资源池，以 2 CPU / 2 GiB 构建预算和单 MSBuild 节点执行 .NET 聚焦用例；双库使用现有共享 SQL Server/MySQL/Redis 测试服务，各用例独立数据库。本记录不是容量结论。

| 命令/场景 | 实际结果 |
| --- | --- |
| `node --test packages/admin-form-designer/tests/source-inventory.test.mjs` | 1/1，338 来源文件逐字节/摘要一致 |
| `pnpm --filter @fullnet/admin-form-designer test -- src/VForm3DesignerHost.test.ts` | 3/3，来源/窗口/通道与卸载拒绝迟到回执 |
| `pnpm --filter @fullnet/admin test -- src/workflow/VForm3RuntimeRenderer.test.ts src/views/WorkflowFormsView.test.ts src/workflow/VForm3WorkflowDesigner.test.ts src/api/workflow-forms.test.ts` | 4 文件 9/9；含正式 Renderer 的只读/Patch 白名单与加载串行、屏蔽迟到变更 |
| `pnpm --filter @fullnet/uniapp test -- tests/workflow-form-model.test.ts` | 4/4，仅验证本次 Vitest 工具链影响的既有模型用例，不代表 VForm 移动端 |
| `pnpm --filter @fullnet/admin build` | 类型检查和生产构建退出 0；原版 direct-eval/Sass 旧写法告警保留，执行局限隔离帧 |
| `pnpm --filter @fullnet/admin exec vite preview --host 127.0.0.1 --port 15173 --strictPort`，随后在 admin-parity 执行 `node node_modules/@playwright/test/cli.js test tests/vform3-full-source.spec.mjs tests/shell-parity.spec.mjs --grep VForm --project vue-admin --workers 1` | 直接读取生产 dist 的 10/10，通过后停止本轮 preview；再补测含 alert 的全部字段场景 1/1 |
| 在项目 `withTestRun` 中 `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release -m:1 -nr:false -v quiet`，再运行程序集 `--filter FullyQualifiedName~Full.NET.UnitTests.Workflow.WorkflowDesignerAssetTests --minimum-expected-tests 11 --no-ansi` | 构建 0 警告/错误，11/11；包括格式/重复键/危险键、非法组件树、合法原版表格行、复杂设计及隐藏依赖 |
| WorkflowDesignerAssetApiTests：精确过滤该类 SQL Server/MySQL 两个方法 | MySQL 1/1、SQL Server 1/1；完整 JSON/数据库读取、修订冲突、完整发布快照、失败保留原设计/上一版本、权限与部分迁移恢复 |
| admin-real-stack：`tests/workflow-forms.spec.mjs --grep '管理员可在严格 CSP 下通过 VForm3' --project vue-admin --workers 1`（withTestRun 重型阶段，独立端口 15489/系统临时运行目录） | 1/1；真实创建、设计资产 API 写入、重载页面、原版回显、UI 保存 Revision 3、UI 发布、读取不可变版本及完整结构比较，控制台无所检查的 CSP/组件错误 |
| `dotnet build src/Modules/Full.NET.Modules.Workflow/Full.NET.Modules.Workflow.csproj -c Release -p:FullNetAotAnalysis=true -m:1 -nr:false -v quiet`，随后恢复普通 restore 图 | 最终结构校验修改后模块 AOT 分析通过，0 警告/错误，55.46 秒；不等同 Host.Api 完整分析或原生发布/E2E |
| 两份 286 迁移调用 `validateSqlNaming`、`validateUuidStorageSql`、`validateSqlComments` | 三项均无违规 |
| `node scripts/openapi/generate-fullnet-client.mjs --check` | 客户端生成零漂移 |
| `pnpm audit:clients` | 精确 Quill Low 限期例外外，无未复核 low+ 公告 |
| `pnpm test:bundle-budgets` | 首屏与独立延迟包预算均通过，完整 VForm 基线如下 |

没有运行全仓测试、整个 Workflow 套件、完整双库矩阵或全部 AOT 原生用例。

## 构建包体

同一 Windows/Node/pnpm、Release Web 生产构建实测；单位均为字节。完整原版功能替代旧 8 KB 精简面板，保留 +5% 相对退化门禁，未改首屏原预算。

| 范围 | minified | gzip | Brotli |
| --- | ---: | ---: | ---: |
| 后台首屏静态 JS 图（68 chunks） | 1,467,884 | 382,157 | 未合计 |
| 隔离运行入口 JS | 2,638 | 1,310 | 1,145 |
| 完整 VForm 主 JS | 3,007,445 | 796,594 | 630,750 |
| 完整 VForm CSS | 483,244 | 63,048 | 46,989 |

主 JS/CSS 的真实新基线写入 [bundle budgets](../../tests/performance/frontend-bundle-budgets.json)，旧 VForm3EsmDesigner 资产断言已替换。Ace worker、字体、示例和 Notices 随完整静态包保留；这些数字不是整个静态目录传输总量，也不证明加载延迟或生产容量改善。

## 失败定位与修正

- 旧 globEager 只编译未运行、Vue 响应式对象 postMessage 克隆失败、开关初始化校验早于父引用、富文本初始恶意 HTML 未同步清理后的字段值：分别修正构建转换、固定协议纯 JSON 载荷、nextTick、富文本值事件；相关浏览器回归通过。
- 导入原版浅覆盖留下上一设计脚本配置：原地清理并恢复默认表单对象，保留 provide 响应性；导入/导出完整结构比较通过。
- 原版 html-text 的 SFC `v-html` 缺字符串表达式转义：通过外部生成适配修正；实际 Vue 3 SFC parse/script/template 编译通过。
- 空画布提示覆盖拖放区域：集成样式修正指针命中和目标高度，真实拖拽新增通过。
- 本地模板 Axios 读取 Cookie 引发沙箱 SecurityError：关闭帧内 XSRF Cookie 读取，模板/预览操作通过。
- 组件遍历略过非法标量/无 type 对象：先新增回归，10 项中 3 项真实失败；补足树结构校验及合法表格行正例后 11/11。
- 正式 Renderer 权限/数据快速切换时可能交错装载：先以延迟回执回归复现，再串行加载不可变快照、装载期间隐藏并禁用旧视图，拒绝旧变更产生 Patch；相关 2/2 及最终管理端构建通过。
- 首轮 .NET 使用通配过滤器没有发现测试，未计通过；改为精确 FullyQualifiedName 表达式。首个单测夹具缺容器 options，按原版合法结构修正。SQL Server 共享 schema 模板文件冲突时改用测试独占新库初始化，未删除共享资源；MySQL 及 SQL Server 分别真实通过。
- 真实页面首轮因编辑器高度挤压关闭按钮超时，改为 flex 有界滚动；第二轮真实业务断言完成但测试登录 addInitScript 访问 opaque iframe localStorage 出错，对共享登录脚本增加仅主窗口执行边界后 1/1。
- JSON 文件导出补测首次把 Element Plus 默认 `OK` 定位成“确定”而超时；按原版实际按钮修正后真实下载/内容比较通过，最终生产产物批次 10/10。

## 未完成与未验证

- **首轮 VF-F4 缺口现已实施：** 原记录中 BindingJson 仅为 Schema、没有目标/绑定修订、静态 RuntimeVersion 的问题，已在继续实施中替换为显式绑定清单和真实产物摘要，验证增量见下文。
- **生产绑定：** 原版设计/预览完整保留，业务投影目前只接受已固化的标量和字符串复选值；精确数值、日期范围/多选和非字符串选项等明确不支持。自定义校验、动态选项和未支持日期形态会明确拒绝业务发布，草稿仍无损保留；当前没有“全部原版输入可生产办理”的结论。真实 Files/目录/新业务连接器尚未接入。
- **覆盖边界：** 继续实施已补全字段属性编辑、跨列拖拽、复制删除、表格合并及双库租户边界/实际办理。未穷举全部属性组合，不同办理人快速切换仍由宿主单测和既有资源授权覆盖，不冒充新增完整浏览器矩阵。
- **原生与审查：** 已补当前 Linux Native AOT 发布和 VForm 新资产/发布/办理/租户边界原生双库 2/2，其它原生矩阵没有扩测。可用 code-review 工具仅能读取远程 PR 检查，不能审查本地差异；没有独立审查证据，本地检查不冒充独立审查。
- **其它：** 本轮不含 Workflow-Vue3 全节点迁移，不含 LogicFlow；uni-app/小程序/Flutter、生产容量保持原状态。没有全量测试或发布结论。

最终整理：已停止本轮 preview/真实栈；固定来源下载副本与任务专属真实栈临时 build 清理被命令策略拒绝（blocked by policy），没有绕过或重试删除，约 632 MiB 仍保留于下述任务目录。原始来源、清单及正式迁移均保留。最终分支仍为 main，HEAD 仍为入口基线。

清理前已确认目标解析为这两个明确任务目录，且无非 PowerShell 进程持有其命令路径。被拒绝的原命令如下；这不是 Auto-review 拒绝，不宣称已清理：

```powershell
$vformCleanupPaths = @('C:\Users\Administrator\AppData\Local\Temp\fullnet-vform3-c67479e-20261024', 'C:\Users\Administrator\AppData\Local\Temp\fullnet-vform3-real-stack-20261024'); foreach ($vformCleanupPath in $vformCleanupPaths) { if (Test-Path -LiteralPath $vformCleanupPath) { $vformResolved = (Resolve-Path -LiteralPath $vformCleanupPath).ProviderPath; if ($vformResolved -ne $vformCleanupPath) { throw 'Unexpected cleanup target' }; $vformReaders = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -notin @('pwsh.exe','powershell.exe') -and $_.CommandLine -like ('*' + $vformResolved + '*') }); if ($vformReaders.Count) { throw 'Task output is still in use' }; Remove-Item -LiteralPath $vformResolved -Recurse -Force; Write-Output ('Removed task-owned temporary output: ' + $vformResolved) } }
```

结果：`rejected: blocked by policy`。当前会话审批策略为 never，不能申请执行审批；没有通过其他解释器或更宽目标重试。

## 继续实施：组件绑定与实际运行产物收口

用户后续明确要求“继续”。同一入口 HEAD 与工作区延续，新增任务快照 `vform3-publish-runtime-closeout-20261024`，未提交、推送或运行全量测试。

### 当前实现

- BindingJson 固化 componentId、designPath、modelPath、fieldKey、fieldType、双向 codec、bindingRevision/bindingHash、Web 目标、组件注册清单、编译器/适配器版本和设计摘要；布局与展示控件不伪造业务字段。
- 发布明确 `targets=["web"]`、`expectedBindingRevision` 与 `rendererHash`。草稿/绑定修订不匹配返回 409，未知目标或错误运行摘要拒绝发布，上一活动版本不变。
- [实际产物清单](../../contracts/workflow/vform3-runtime-release.json) 由生产构建逐文件生成，涵盖入口、JS/CSS、字体、Ace worker、模板和 Notices；其 SHA-256 为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。前端常量与后端嵌入相同清单，不再以静态版本号冒充实际产物固定。
- 更新入口 `node scripts/workflow/vform-runtime-release.mjs --update` 只写 package dist/runtime；部署核验 `node scripts/workflow/vform-runtime-release.mjs --verify <实际 vform3 目录>`。构建、dev server 和 preview 自动核对，摘要不符停止启动/构建。实例启动、办理读取及含补丁的审批/退回核对发布绑定与当前运行版本；缺少 VForm 快照不得走程序化 Schema 路径，程序化消费者继续使用明确业务 Schema。
- Renderer 只消费授权绑定，保留字符串、布尔和字符串数组，不把选项数字/布尔转为字符串。null 与文本空串分别保留，原版数字/日期/时间清空到业务 null 的规则显式固定。整数须 precision=0，服务端及客户端均拒绝超出 ±9,007,199,254,740,991 的值。精确小数/金额、非字符串选项、自定义验证、动态选项及不支持的时间形态明确拒绝业务发布，完整设计不裁剪。
- 隐藏节点和绑定元数据被剔除；双库实测 Tenant 不能读写 Host 资产。加载串行并隔离迟到回执，未知/只读/隐藏 Patch 不进入提交，服务端保持权威校验。

### 新增发现和修正

1. 外层 Vite dev 模式污染内层构建，实际字节与生产包不同；修改 NODE_ENV 仍不能消除缓存的开发编译状态。现用独立 Node/Vite 生产进程构建，开发与交付统一，字节门禁通过，没有放宽摘要比较。
2. 显式载入新资产前恢复旧备份，会在退场动画中保留旧控件并允许误选。使用原版 resetFormJson 初始化空画布，补独立撤销起点，并禁用退场节点指针事件；连续切换与撤销重做通过。
3. ElSwitch 挂载默认值编辑器时把 null 写成 false。只在集成构建转换中使用布尔显示投影，用户触发才回写，原上游文件不改；23 字段编辑后其余属性逐项结构比较通过。
4. 新增真实办理已到达提交/数据库回读，但控制台发现既有待办页漏导入 ElDrawer；已修正，最终真实场景 1/1。
5. Host.Api AOT 分析实际报出两项 IL2026/IL3050：`JsonArray.Add<T>` 泛型重载需要裁剪/动态代码。改为显式 `JsonNode` 重载后重跑完整 Host.Api 分析，0 警告/错误，36.44 秒；没有增加 suppression。
6. Renderer 未就绪或摘要不匹配时，即使已有必填值也不能审批。增加就绪状态上报和页面操作门禁，并提供本地化诊断；相关回归通过。
7. ElDrawer 正式注册后的真实用例在审批点击后等待请求超时；接口桩浏览器用例复现，按钮显示可用，但父页面捕获不到鼠标事件。隔离帧焦点与滚动内容中的操作区导致点击目标移位；将操作区移到抽屉固定 footer 后，同一回归通过。已移除诊断日志/事件探针，最终仅该浏览器回归重复两次 2/2；相邻待办页面单测 12/12。没有靠延时、强制点击或放宽请求断言换绿。

### 聚焦结果

- 来源/产物 Node 2/2；包括实际文件变化、额外插入同名嵌套清单时，核验明确失败。
- 宿主 4/4；包括旧摘要 Ready 被拒绝且不能调用数据接口。
- `pnpm --filter @fullnet/admin test -- src/workflow/vform3-values.test.ts src/workflow/VForm3RuntimeRenderer.test.ts src/workflow/VForm3WorkflowDesigner.test.ts src/views/WorkflowFormsView.test.ts src/views/WorkflowTodosView.test.ts src/api/workflow-forms.test.ts`：最终相关 6 文件 29/29，覆盖类型/空值/精度、串行加载、Patch 白名单、发布请求和 Renderer 不可用时禁用审批。
- 生产构建上的浏览器 12/12：23 字段原版预览/属性编辑、布局、跨列拖动、复制删除、合并回显、工具栏、实际文件下载、SFC 编译及隔离。
- 新增抽屉回归：`tests/shell-parity.spec.mjs --grep 'VForm 3 待办抽屉' --project vue-admin --workers 1 --repeat-each 2`，2/2；最终生产 dist 再精确运行同一用例 1/1，3.9 秒，通过后停止 preview。真实鼠标点击与实际请求体检查，不以调用组件方法替代。
- 最终服务端精确过滤 `Full.NET.UnitTests.Workflow.WorkflowDesignerAssetTests`：19/19；精确过滤 `Full.NET.IntegrationTests.Workflow.WorkflowDesignerAssetApiTests`：SQL Server/MySQL 2/2，0 跳过，2 分 16.398 秒。涵盖实际原版版本启动/办理/审批、错误目标/产物拒绝、最终绑定修订 409、模拟快照不匹配时读写阻断、迁移恢复和 Tenant→Host 越界拒绝；两份测试程序集 Release 构建均 0 警告/错误。
- `pnpm --filter @fullnet/admin build`、`pnpm test:bundle-budgets` 与实际字节核验通过，既定 +5% 相对预算通过；最终首屏 minified 1,468,139/gzip 382,234；完整 VForm JS 3,007,864/gzip 796,809，CSS 483,306/gzip 63,053。没有抬高预算或宣称性能改善。
- `node scripts/openapi/generate-fullnet-client.mjs --check` 零漂移；最终 Git 可见来源 338/338，强制入索引的 6 个原始分发文件与清单摘要完全一致。
- 最终 admin-real-stack 在 `withTestRun` 重型阶段、端口 15489、复用任务专属临时构建目录，执行 `tests/workflow-forms.spec.mjs --grep '管理员可在严格 CSP 下通过 VForm3' --project vue-admin --workers 1`：1/1，2.2 分钟（包含栈初始化）。包含完整设计 API/数据库往返、页面重开、UI 保存 Revision 3/UI 发布、冻结版本结构比较，以及原版 Renderer 实际填入新值、真实鼠标审批、请求 Patch 和数据库提交值回读；所检查 CSP/组件错误为空。没有将全控制台无告警作为结论，既有页面 loading 指令和页签图标警告不在该断言范围。
- Host.Api 完整 AOT 分析和最终服务端复核通过；当时未验证的 Linux 原生发布/进程在下一节补上，独立审查仍未验证，Quill Low 限期例外不变。最终 preview 和真实栈已停止；此前明确目录清理被策略拒绝的记录继续有效，没有改用其它命令重试删除。

## 继续验证：Linux 原生执行

用户继续授权后，新增任务快照 `vform3-native-runtime-20261024`。复用现有 NativeApiProcessHost、独立数据库与 JIT Migrator；精确选择器和最低发现数维护于 `eng/testing/test-matrix.json` 的 `nativeAotVFormIntegration`。没有运行完整 Native 或 Workflow 套件，没有新增业务连接器。

### 环境与失败证据

- Docker Desktop Linux/WSL2，SDK 10.0.402、目标运行时 10.0.13、linux-x64；SDK 镜像 `fullnet-ai-native-sdk-402:bc74b9f1dc`，镜像 SHA-256 `4488e0c3b77341253b127f67fcc8f7940caf19c1c8b69a86d8b5c5c6bea36dc1`，含 clang/ld。本批容器限制 2 CPU，VM 可见内存 19.29 GiB，没有设置额外 GC 堆参数或修改生产编译选项。
- 源码只读挂载 `/src`；复用系统临时目录 `fullnet-vform3-native-20261024`，分别覆盖容器内已有 `.tmp` 和 `artifacts` 挂载点，原生与 JIT 构建图分离。首次因只读源目录下不存在新的挂载点而返回 Docker 125；改用已存在的挂载点后启动，没有复制整仓或删除既有输出。
- 第一份产物对应 `main@a77902afd87af1662af3b43206d85160eeec566b` 加工作区源码输入摘要 `3f13bc3e93b46541f2da7613a55bdf67d82e3086379b4de92f22d24b04c2be8d`，构建前后核对 3,683 个源文件/契约及根配置。实际 ELF 为 143,591,584 字节，SHA-256 `507c6ee79d2553a9ef27b649ed2680c7c1e2b2fbb73ce75e12136da13b22f4f5`；9 条 IL 告警均通过现有“程序集+告警码”清单，不以增加 suppression 换绿。
- Linux 测试驱动和 Migrator 构建均 0 警告/错误；发现精确为 2 个新增用例。首次实际执行 **0/2 通过、2 失败、0 跳过，2 分 49.420 秒**：两库均完成设计保存/读取及修订冲突后，在错误 Renderer 摘要发布请求处预期 400、实际 500，日志为 `NotSupportedException`。发布成功不能替代这次失败的运行验收。
- 原始失败清单、发布日志、TRX、HTTP 响应和进程日志保留在上述系统临时目录；原生程序在用例结束时停止，数据库容器由测试夹具回收，共享开发容器未删除。

### 根因与修复

ASP.NET Core 的 ProblemDetails writer 同时具备 `IDictionary<string, string[]>` 和 `IReadOnlyDictionary<string, string[]>` 接口元数据；错误扩展中的实际 `Dictionary<string, string[]>` 没有显式元数据，禁用反射回退时查找最近接口产生歧义。最初只序列化外层字典或独立 ProblemDetails 对象的检查没有复现，不能作为真实 writer 的通过证据。改为实际执行 `IResult.ExecuteAsync`、注册 ASP.NET Core ProblemDetails 服务并移除反射 resolver 后，两条回归均真实失败，错误明确为 `Ambiguous matches when resolving JsonTypeInfo metadata`。

修复位于 Hosting 自有 JSON 上下文：显式登记该具体校验字典类型，不将通用错误传输责任放入 Workflow。17 项 `StandardApiResultMapperTests` 与 `FullNetJsonOptionsTests` 通过，包括普通/结构化校验错误的真实 writer；Hosting 构建及复用未改依赖的单测构建均 0 警告/错误。没有全量测试或 JSON 反射回退。

托管聚焦命令：`dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter "FullyQualifiedName~Full.NET.UnitTests.Hosting.StandardApiResultMapperTests|FullyQualifiedName~Full.NET.UnitTests.Hosting.FullNetJsonOptionsTests" --minimum-expected-tests 17 --no-ansi`；实际 17 成功、0 失败、0 跳过。

修复后的源码输入摘要为 `df362dba58eed8daeb7946b4a7671817f8dbb86eb19a544ee2707818465c0d6a`；实际 Web Runtime 摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。完整重新发布生成 143,600,128 字节的 linux-x64 ELF，SHA-256 `1c287916ac0f87503a27a707c159b21c7a87a04fefee8c7689b4f35db6ac8a26`，与旧产物不同；源码输入核验一致，9 条既有许可 IL 告警检查通过，未新增告警豁免。更新后的 Linux 测试驱动和 Migrator 构建均 0 警告/错误。

随后两库已通过原先失败的错误摘要 400、绑定修订冲突、正常发布/冻结、启动及授权读取，另在隐藏字段 Patch 处暴露用例预期错误：既有业务策略拒绝使用 `BusinessRule`/422，用例误写为 400。该轮 0/2、0 跳过，2 分 47.295 秒；只把断言修正为准确的 422，保留后续合法审批以证明失败没有消耗修订，未修改业务状态码或放宽断言。原始 TRX 与日志另存为 fixture 失败证据。复用同一已核验新 ELF 与未变化的依赖，仅重编译测试驱动后重跑，最终结果见下文。

修正状态码后的两库已完成合法审批和文本/复选数组提交值回读，随后因租户夹具错误假设 `acme` 而失败：原生 Migrator 使用真实 Development `local` 种子，普通 API factory 才另外供给 Acme。该轮 0/2、0 跳过，2 分 47.312 秒，不能记整用例通过。改为复用现有 `NativeApiE2EAssertions.EnterLocalTenantAsync`，仍要求切换后 Host 设计资产 GET/PUT 均返回 404；生产实现及 ELF 不变，仅重编译并重跑这两个用例。

实际原生发布命令（容器内，输出挂载至上述系统临时目录）：

```bash
dotnet publish src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release -r linux-x64 --self-contained true -p:FullNetPublishMode=NativeAot -p:RuntimeFrameworkVersion=10.0.13 -p:UseArtifactsOutput=true -p:ArtifactsPath=/src/.tmp/aot-build -o /src/.tmp/publish -m:1 -nr:false --nologo
```

`FullNetPublishMode=NativeAot` 按仓库 targets 同时启用完整可达 .NET 闭包的 AOT/Trim 分析、绑定与 RequestDelegate 源生成；没有用旧产物验证新源码。

### 最终结果与边界

- 精确执行 `dotnet /src/.tmp/jit-build/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll --no-ansi --filter FullyQualifiedName~native_artifact_preserves_full_vform_design_and_submission --minimum-expected-tests 2 --timeout 15m --results-directory /src/.tmp/results --report-trx --report-trx-filename vform-native-complete.trx`。选择值由上述 matrix 项生成，不运行整个 Native 集合。
- **SQL Server/MySQL 最终 2/2，通过且 0 跳过，2 分 54.692 秒。** 原始 TRX 核对 total/executed/passed 均为 2，failed/error/timeout/aborted/inconclusive/notExecuted 均为 0；两个结果均 Passed。MySQL 1 分 50.344 秒，SQL Server 1 分 2.784 秒。最后仅重编译变化的测试驱动，复用当前 ELF、已完成的依赖和 Migrator；构建 0 警告/错误。
- 场景包含完整原版 JSON 的 API/数据库往返和冻结版本、保存/绑定修订冲突、错误产物摘要拒绝、非空字符串复选值的绑定编译、实际启动与办理读取、隐藏组件/默认值/提交值/绑定裁剪、隐藏 Patch 的 422 拒绝、失败后仍可用原修订完成合法审批、文本/数组提交值回读，以及真实切换 Development `local` 租户后的 Host 资产 GET/PUT 404。两个原生进程均正常启动/停止，所检查运行故障标记为空。
- 完成后再次核对 ELF SHA-256 与 3,683 项源码输入摘要，均未变化；Native API 当前只声明这条 VForm 路径的双库运行证据，不外推为全部 Workflow、其它 Native Provider、Worker 或跨端/生产容量验收。
- 以精确路径核验、任务清单归属及无进程/容器挂载占用为前置，用原生 PowerShell `Remove-Item -LiteralPath ... -Recurse -Force` 清理本轮专属 `aot-build`、`jit-build`、`publish` 三个临时输出目录，实际成功释放约 3.82 GiB。保留小型输入/产物摘要、源码清单、发现结果、成功及失败 TRX 和必要原始日志。没有重试此前被策略拒绝的另两个任务目录清理，未删除源码或共享开发容器。
- 独立审查仍不可用；未新增业务连接器、未发布站点/服务、未提交或推送代码。最终分支 main，HEAD 保持入口基线。

## 继续复查：隔离通信接收边界

用户再次要求继续后，沿用已完成的 VF-F1–F4 范围复查，入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，任务快照为 `vform3-frame-receive-20261024`。没有提前扩展新的业务连接器或 Workflow 节点。

发现宿主 `frame-client.ts` 对调用回执没有长度检查；变更消息中的循环引用和 BigInt 会让 JSON 序列化抛出宿主异常；变更回调也先于 Ready 摘要核验执行。浏览器的结构化克隆允许前两种值，不能仅凭来源窗口、opaque Origin 和通道匹配就认定消息符合 JSON 协议。

修复在宿主接收边界统一检查完整消息的 JSON 可序列化性和已有的 2,100,000 字符上限，保留变更值的 2,000,000 字符上限；异常调用回执立即拒绝对应 Promise 并清除超时定时器，非法变更不进入业务回调。只有运行摘要通过 Ready 核验后才转交变更，后续合法消息不受影响。这里的字符数是序列化字符串长度；服务端原有 UTF-8 字节上限仍独立执行，没有放宽设计资产限制。

针对性验证与边界：

- `pnpm --filter @fullnet/admin-form-designer test -- src/VForm3DesignerHost.test.ts`：修复前最终复现 **6 失败、5 通过**，明确捕获提前变更、循环引用/BigInt 异常和非法调用回执被当作成功的问题；修复后 **11/11 通过**，退出码 0，包括异常后合法消息与清理定时器。最初回执断言会打印巨大测试字符串并异步报告断言拒绝，已改为先收集结果码再断言，最终 RED/GREEN 没有该测试夹具问题。
- `pnpm --filter @fullnet/admin-form-designer build`：宿主类型检查通过。`pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts src/workflow/VForm3RuntimeRenderer.test.ts`：两个直接集成组件 **4/4 通过**。
- `pnpm --filter @fullnet/admin build`：管理端类型检查及生产构建通过。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层运行摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`；只修改父宿主接收逻辑，上游来源、帧内原版运行代码及后端输入没有变化。
- Playwright CLI 使用独立 Edge 会话 `vform3-frame-20261024`，加载当前 Vite 编译的真实 `VFormFrameClient`，通过 `sandbox="allow-scripts"` 的 srcdoc 夹具实际发送结构化克隆消息。Ready 前及错误摘要后的变更数为 0；正确 Ready 后只收到一条合法变更；循环引用/BigInt/超限变更没有触发宿主页面异常，超限回执返回 `client.vform3_payload_too_large`，非 JSON 回执返回 `client.vform3_operation_failed`，随后完整 JSON 回执结构一致。实际接收 Origin 只有 `null`，pageerror 为 0。错误摘要产生的一条 `client.vform3_runtime_mismatch` 是预期诊断，没有当成页面异常忽略。
- 浏览器脚本通过 `npx --yes --package @playwright/cli playwright-cli -s=vform3-frame-20261024 run-code --filename protocol-browser.js` 执行，随后用 `eval` 回读 Ready、变更、完整配置和 Origin。小型脚本及 CLI 记录位于系统临时目录 `fullnet-vform3-frame-20261024`；该检查验证真实浏览器通信边界，不冒充新增原版控件/UI/双库全流程验收。未改输入的原版控件、双库与 Native 证据复用上文结果，没有重复运行完整集合。
- 本轮没有新增依赖或修改运行包契约；独立审查仍未取得证据。收尾停止独立浏览器会话及本轮 Vite 服务，检查 UTF-8、相关文档链接及 `git diff --check`，未提交或推送。

## 继续复查：载入与保存读取的异步时序

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，保留已有差异，创建任务快照 `vform3-designer-load-20261024`。本轮只修复 `VForm3WorkflowDesigner` 与其直接受影响路径，没有新增生产业务绑定。

原实现先等待 `nextTick` 再登记载入 Promise，Ready/属性更新后立即读取可能越过新载入任务；读取回执等待期间切换资产，也未检查回执属于哪一次载入。与此同时，编辑禁用只接续外部 busy，载入排队及失败时仍可能操作旧画布。Host 原有的 Ready 校验保持不变，本轮修复的是 Workflow 层的资产协调。

修复将完整输入快照与串行载入任务在首次 await 前登记，按资产代次维护载入/失败状态；旧任务结束不再提前解除新任务的禁用状态。读取先接收本轮父属性更新，再等待最新载入；等待载入或读取回执期间若资产改变，重新读取最新画布。宿主错误及卸载使读取失效，不交付残留结果；载入失败保留拒绝状态，后续正确资产可恢复，原版 JSON 不被裁剪或重建。

针对性验证：

- `pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts`：初始回归 **7 失败、1 通过**，包括实际观察到读取先于载入、切换后仍返回第一份配置及载入期间未禁用等。修复后新增过期失败、不可序列化资产和卸载边界，最终该文件 **11/11 通过**。
- 最终合并直接调用方检查：`pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts src/views/WorkflowFormsView.test.ts`，**2 文件 15/15 通过，退出码 0**。覆盖首次载入、同一轮更新即读取、串行排队、载入/读取期间切换、失败重试、过期失败不污染新资产、宿主错误及卸载。
- `pnpm --filter @fullnet/admin build`：类型检查及生产构建通过。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层运行摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。未改变后端、SQL、原版来源、运行包或依赖，复用已有双库/Native 结果，没有扩测无关集合。
- Playwright CLI 独立 Edge 会话 `vform3-load-20261024`，通过 `npx --yes --package @playwright/cli playwright-cli -s=vform3-load-20261024 run-code --filename load-browser.js`，挂载当前真实 Vue 包装组件和原版 VForm iframe。仅用测试运输门闩暂存 `setFormJson` 的真实消息，未替换组件或修改发布文件；等待期间 `getFormJson` 请求数为 0，读取未结束，遮罩及 iframe inert 均生效。重新交付原始消息后，嵌套 widgetList、根扩展及输入表单配置逐项一致，真实画布显示第二份标题。
- 同一浏览器场景继续发送帧明确拒绝的无效资产，读取返回 `client.vform3_operation_failed` 且画布保持禁用；更新正确资产后读取恢复，扩展配置为新内容。Ready 前读取拒绝为 `client.vform3_not_ready`；最终报告 `passed=true`、三项结构比较均 true、pageerror=0。
- 浏览器夹具先后两次失败：先等待了被父组件透传属性覆盖的旧 data-testid 而超时；随后过晚注册运输门闩，实际帧监听器已先处理消息，未真正制造延迟。核对实际 DOM 与消息次序后修正夹具，在帧脚本执行前安装门闩，保留原等待/结构断言，最终场景通过。没有改生产代码去满足错误夹具，没有增加固定延时或强制点击。控制台仍存在 Vite HMR WebSocket 的本地网络访问错误及原版 Element Plus 弃用警告；本次没有声明全控制台无告警。
- 小型脚本与 CLI 日志保存在系统临时目录 `fullnet-vform3-load-20261024`。这次浏览器检查证明加载/读取协调，不冒充新增 API/数据库往返验收。收尾关闭本轮独立浏览器与 Vite 服务，检查 UTF-8、文档链接和 Git 空白；独立审查仍未取得证据，未提交或推送代码。

## 继续复查：设计资产与期望修订必须来自同一响应

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，任务快照为 `vform3-asset-revision-20261024`。本轮复查表单页面的保存、发布和关闭衔接，实际修复限定于 `WorkflowFormsView` 打开编辑器时的读取一致性。

页面原来并行请求定义详情与设计资产，载入资产的 JSON 却使用详情的 draftRevision 保存。两次读取遇到并发更新时，可能把旧设计与较新的修订拼接，削弱保存 CAS 的预期作用；相反方向会造成不必要的冲突。资产尚未创建时，较早的 404 还可能与较新的定义详情拼成空画布重建稿。

现在先读取定义，再读取设计资产；有资产时，设计 JSON 与 ExpectedRevision 始终取自同一次资产响应，没有资产时才沿用先前定义的修订与明确的重建标识。这不提供跨请求数据库快照，也不放宽服务端 CAS：之后再发生并发写入时仍应冲突。后端资产查询本身已经返回设计 JSON 和定义修订，本轮只修复前端错误拼接，没有改 API、SQL、事务或运行包。

针对性验证：

- `pnpm --filter @fullnet/admin test -- src/views/WorkflowFormsView.test.ts`：修复前 **3 失败、4 通过**，明确观察到详情修订 5/资产修订 2 时提交 5、详情 2/资产 5 时提交 2，以及详情读取期间首次创建的资产被空重建稿替代。修复后 **7/7 通过，退出码 0**，包括原有权限、完整配置保存、冲突保留本地设计及发布行为。
- `pnpm --filter @fullnet/admin build`：类型检查及生产构建通过。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。没有重复运行未改输入的双库/Native 或全仓测试。
- Playwright CLI 独立 Edge 会话 `vform3-revision-20261024`，执行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-revision-20261024 run-code --filename revision-browser.js`。挂载真实表单页面与原版 iframe，HTTP 使用模拟响应控制时序，真实点击编辑/保存并核对实际请求体；**3/3 场景通过，pageerror=0**。
- 浏览器场景：详情 5/资产 2 时实际提交 2，模拟 CAS 返回 409，权威夹具仍为修订 5，画布及完整本地 JSON 保留；详情 2/资产 5 时提交 5并显示保存后的 Revision 6；详情等待期间资产由不存在变为修订 3，资产读取未越过定义请求，最终载入完整新资产并提交 3、显示 Revision 4。三次请求的 widgetList、根扩展及自定义表单配置均保留。这里的“权威修订”是 HTTP 夹具状态，不能替代真实数据库验收；服务端既有 CAS 证据复用前文。
- 浏览器初次夹具提供空业务字段列表，被前端 Schema 校验拒绝，未进入编辑器；改用合法文本字段后重跑。Edge 本地网络策略导致 Vite HMR 控制通道报错，首轮诊断日志重复增长；仅在系统临时配置关闭测试服务的 HMR，保留原生产配置和断言。最终仍有既有 loading 指令/原版组件警告、开发通道及预期 409 控制台错误，本轮没有声明全控制台无错误。
- 小型浏览器脚本、临时 Vite 配置和原始日志保存在系统临时目录 `fullnet-vform3-revision-20261024`。尝试保留原始诊断前缀后删除重复 HMR 日志，但整条命令被策略拒绝，未执行前缀提取或删除；10,279,336 字节（约 9.8 MiB）的重复日志仍保留。没有重试或改用其它解释器绕过。收尾关闭本轮浏览器及服务，核对 UTF-8、文档链接和 Git 空白。没有新增业务功能或提交/推送，独立审查仍未取得证据。

本轮日志清理被拒绝的原始 PowerShell 命令：

```powershell
$vformRevisionRoot = (Resolve-Path -LiteralPath 'C:/Users/Administrator/AppData/Local/Temp/fullnet-vform3-revision-20261024').ProviderPath; $vformRepeatLog = (Resolve-Path -LiteralPath 'C:/Users/Administrator/AppData/Local/Temp/fullnet-vform3-revision-20261024/.playwright-cli/console-2026-10-23T22-58-21-371Z.log').ProviderPath; if (-not $vformRepeatLog.StartsWith($vformRevisionRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Log is outside task directory' }; $vformRevisionReaders = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -notin @('pwsh.exe', 'powershell.exe') -and $_.CommandLine -like ('*' + $vformRevisionRoot + '*') }); if ($vformRevisionReaders.Count) { throw 'Task output is still active' }; $vformFailurePrefix = Join-Path $vformRevisionRoot 'hmr-console-failure-prefix.log'; Get-Content -LiteralPath $vformRepeatLog -TotalCount 60 | Set-Content -LiteralPath $vformFailurePrefix -Encoding utf8; if ((Get-Item -LiteralPath $vformFailurePrefix).Length -eq 0) { throw 'Failure evidence was not retained' }; $vformRemovedBytes = (Get-Item -LiteralPath $vformRepeatLog).Length; Remove-Item -LiteralPath $vformRepeatLog -Force; Write-Output ('Kept raw failure prefix; removed repeated task log bytes: ' + $vformRemovedBytes)
```

结果为 `rejected: blocked by policy`，不是 Auto-review 拒绝。当前会话使用 never 审批策略，未申请额外执行审批；此前其它任务目录的清理拒绝记录也保持有效。

## 继续复查：缓存页面的身份与权限上下文

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，任务快照 `vform3-page-context-20261024`。本轮修改仅涉及 `WorkflowFormsView`、直接回归和相关文档。

管理壳层通过 KeepAlive 按路由缓存页面实例。原表单页没有在账号、会话、租户或权限变化时清空旧编辑器；尚未结束的读取、帧读取及保存回执也没有上下文校验，可能在切换后重新展示旧资产，或以新上下文继续发出旧画布的保存。此处修复客户端状态和异步协调，Endpoint 的授权及可信租户边界保持权威。

当前行为：

- 以账号 ID、会话 ID、TenantId、scope、切换状态及本页读写权限构成稳定上下文。实际变化时先清空列表、版本、编辑器和错误，取消该上下文的读取；有读取权限且不在切换时，再获取当前范围的数据。普通昵称/ProfileVersion 刷新不清空未保存设计。
- 所有异步结果及错误在当前代次有效时才写入页面；旧操作的 finally 不解除新请求的 busy。打开编辑器的第二次读取、发布/删除后的刷新同样受保护，卸载时取消读取并忽略旧回执。
- 读取隔离帧期间若上下文变化，不再发起保存。已发出的创建/保存/发布/状态/删除请求由服务器继续执行，前端只忽略过期结果，不把 AbortController 或页面关闭当作写入回滚。
- 删除版本失败时不再无条件弹出成功提示；成功后才刷新相应版本/定义并提示一次。注册本页 ElLoading 指令，接续真实 busy 的视觉反馈。

针对性结果：

- `pnpm --filter @fullnet/admin test -- src/views/WorkflowFormsView.test.ts`：原测试夹具只清空调用记录，会残留排队 mock 返回，已改为逐用例 reset 后重新确认复现；修复前 **10 失败、8 通过**，明确捕获旧编辑器残留、未取消读取、跨上下文继续保存、迟到成功回填和错误成功提示。修复后首轮 18/18；补取消/恢复、卸载迟到错误与成功删除边界后，最终 **21/21 通过，退出码 0**。
- `pnpm --filter @fullnet/admin build`：类型检查及生产构建通过；最终增加测试边界后另执行 `pnpm --filter @fullnet/admin typecheck`，退出码 0。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。后端、SQL、运行包和依赖未变化，未重复双库/Native 或其它页面集合。
- Playwright CLI 独立 Edge 会话 `vform3-context-20261024`，运行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-context-20261024 run-code --filename context-browser.js`。真实 Vue 页面和原版 iframe，HTTP 响应受控，使用真实点击及属性面板编辑。昵称刷新后未保存标题仍在且未重载列表；撤销编辑权限后 iframe 被移除；租户切换后旧 GET 实际返回 `net::ERR_ABORTED`；已经发出的 PUT 在模拟权威状态推进至 3 后，迟到响应仍未重开旧编辑器或回填旧列表。综合场景通过，pageerror=0。
- 上述浏览器报告的写入修订属于 HTTP 夹具，不是新数据库证据；身份切换由测试注入会话快照控制，不冒充实际登录/租户切换协议验收。控制台仍有开发 HMR 通道错误及原版 Element Plus 弃用警告；本页未再出现 loading 指令未注册警告，不宣称整个控制台无告警。
- 小型脚本、临时 Vite 配置及约 37 KiB 原始日志位于系统临时目录 `fullnet-vform3-context-20261024`。关闭本轮浏览器和 Vite 服务，核对 UTF-8、文档链接与 Git 空白；之前被策略拒绝的其它日志/目录清理未重试。未提交或推送；现有独立审查工具仍仅支持远程 PR 检查，独立本地差异审查未完成。

## 继续复查：正式审批页的上下文与抽屉生命周期

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，任务快照 `vform3-todo-context-20261024`。仅修改 `WorkflowTodosView`、直接回归和本计划/记录，未改变 API、数据库、依赖、上游源码或内层运行包。

审批页面也由 KeepAlive 保留。原实现仅在深链切换时增加详情代次，未监听身份/租户/权限；旧表单可继续显示，迟到审批回执会关闭新详情。列表的 catch 检查了后来请求的控制器，finally 又无条件解除 busy，旧请求因此可污染新读取。浏览器还暴露了独立时序：同一抽屉先关闭再接收新详情时，旧关闭事件可能清除新任务。

修复行为：

- 账号、会话、租户/作用域、切换状态及本页读/通过/驳回/退回/加签权限变化时，先清空列表、表单、意见、辅助数据及错误，取消旧列表/详情/辅助读取，再按当前权限加载。切换中或无读取权限不发起读取，普通资料刷新保留未提交字段与意见。
- 审批、驳回、退回、加签及取消加签共享回执边界，同时校验上下文与详情代次。过期成功/错误/finally 不关闭新详情、不弹旧提示、不解除新操作 busy。实际已发出的写入仍交由服务端处理，不宣称取消读取能回滚写入。冲突刷新后仅在原上下文/详情代次保留原始 409。
- 列表结果、错误及 busy 归发起请求的控制器所有。详情和辅助请求使用同一可取消信号，关闭或卸载时取消。执行动作同时复查精确权限及非历史状态；服务端授权仍是安全权威。
- 抽屉仅随当前详情创建，并以任务 ID 区分实例，旧实例的迟到关闭事件不能清空新表单；当前关闭按钮正常生效。注册 ElLoading 指令，使页面原有加载反馈真实生效。

针对性证据：

- `pnpm --filter @fullnet/admin test -- src/views/WorkflowTodosView.test.ts`：首次为夹具错误，`resetAllMocks` 重置了全局 `matchMedia`，27 项均在挂载阶段失败；改为仅重置本文件 API mock 后，修复前 **14 失败、13 通过**，才计作实际缺陷复现。首轮修复 27/27，补权限/作用域、辅助读取、加签成功、无权限及卸载边界后 36/36。
- 浏览器复现抽屉问题后，`pnpm --filter @fullnet/admin test -- src/views/WorkflowTodosView.test.ts -t 旧抽屉` 为 **1 失败、36 未选中**；旧抽屉发出关闭事件后新详情消失。绑定详情生命周期后，完整目标文件最终 **37/37 通过，退出码 0**，并覆盖当前抽屉正常关闭。未选中的用例不计为通过。
- `pnpm --filter @fullnet/admin typecheck` 通过；抽屉修复后重新执行 `pnpm --filter @fullnet/admin build`，类型检查及生产构建通过。内层摘要保持 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`，通过实际产物核验。没有重复未变化的后端、双库/Native 或其它页面测试。
- Playwright CLI 独立 Edge 会话 `vform3-todo-context-20261024`，执行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-todo-context-20261024 run-code --filename todo-context-browser.js`。真实 Vue 页面/路由、原版 VForm 运行 iframe 与真实 HTTP 适配层，HTTP 响应模拟控制时序。普通资料刷新保留实际字段与意见；撤销读取权限后旧 iframe/列表消失且不重读；租户切换后旧 runtime GET 实际 `net::ERR_ABORTED`；实际原版输入产生 `fieldPatch.summary=实际审批补丁`，随意见进入审批 POST；切换第二条深链后释放第一条审批响应，新详情保留且无旧成功提示/额外列表刷新；当前抽屉关闭正常。综合场景通过，pageerror=0。
- 浏览器首轮过早用通用 textbox 定位器命中了原版设计器初始界面的三个文本框，属于夹具定位错误；改为等待“申请内容”后，后续运行实际复现抽屉问题，修复后通过。控制台仍有本地开发通道网络错误及原版 Element Plus 弃用警告，不宣称控制台无告警。会话快照为测试注入，不冒充真实身份切换协议；模拟审批响应也不算新的数据库证据。
- 小型浏览器脚本、构建日志及最小失败日志保存在系统临时目录 `fullnet-vform3-todo-context-20261024`。收尾关闭该浏览器与 Vite 服务，检查 UTF-8、文档链接及 Git 空白。既有策略拒绝的日志/目录清理未重试。未提交、推送或扩大到后续工作流节点迁移；独立审查工具仍只有远程 PR 检查，本地独立审查要求仍未满足。

## 继续复查：正式 Renderer 的宿主故障与过期队列

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，快照 `vform3-renderer-failure-20261024`。本轮仅修改 `VForm3RuntimeRenderer`、其直接回归及计划/验证记录，沿用已启用的缺陷定位、回归测试与完成前验证流程。

原 Renderer 的宿主错误处理只设置提示，没有撤销 Ready 或使载入代次失效。随后属性刷新会清除错误、重新 render 并恢复可办理状态，即使宿主没有重新就绪。载入队列也仅检查卸载，没有检查投影代次；在最新投影校验失败或宿主报错后，先前排队的旧投影仍会发送到隔离帧。

当前处理：宿主错误立即撤销就绪、使全部载入代次失效并清空 Patch；属性刷新不能代替 Ready，故障期间保持隐藏/禁用和不可办理。排队任务在实际发送前再次检查当前代次、就绪状态及宿主引用，跳过已过期投影；异步成功、失败和 finally 仅更新仍有效的实例/代次。新的有效 Ready 加载最新快照后才恢复；实际页面可关闭并重开详情取得新宿主，不把合成错误后的旧 iframe 宣称为已修复。普通投影载入错误与宿主通信故障仍分别处理，保留原版控件与业务字段协议。

针对性证据：

- `pnpm --filter @fullnet/admin test -- src/workflow/VForm3RuntimeRenderer.test.ts`：修复前 **3 失败、5 通过**，分别复现宿主错误后属性刷新擅自恢复、宿主错误后发送旧排队投影，以及最新摘要无效后发送旧投影。另覆盖旧载入失败不阻断当前投影、卸载不再发送或恢复、只读字段及摘要不匹配边界。
- 修复后 `pnpm --filter @fullnet/admin test -- src/workflow/VForm3RuntimeRenderer.test.ts src/views/WorkflowTodosView.test.ts`：**45/45 通过、退出码 0**（Renderer 8、直接调用审批页 37），未运行其它页面或全仓测试。
- `pnpm --filter @fullnet/admin build`：类型检查及生产构建通过。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层摘要仍为 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。上游、依赖、运行包及后端/SQL 输入未修改，双库/Native 复用前文证据。
- Playwright CLI 独立 Edge 会话 `vform3-renderer-failure-20261024`，执行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-renderer-failure-20261024 run-code --filename renderer-failure-browser.js`。挂载真实 Vue Renderer 与原版 opaque iframe，夹具延迟父端接收实际 render ACK，再通过原始帧原生 postMessage 释放该回执，保留真实 source/origin；同时注入 iframe error。过期队列没有形成第二个请求 ID，迟到 ACK 未恢复就绪，帧保持隐藏和 inert、Patch 为空。重新挂载显示最新数据，实际输入生成补丁；再注入错误并刷新属性，原版帧实际发出的迟到 changed 消息被丢弃；再次重挂载后正确显示最新投影。综合场景通过，pageerror=0。
- 浏览器首轮在输入后未离焦，原版尚未触发 change，等待 Patch 超时；补入正常离焦后重跑通过，没有改变生产控件或断言。故障是夹具注入、ACK 是受控延迟，不能据此宣称真实网络故障、真实登录或数据库事务验收。控制台仍有开发通道网络错误及原版 Element Plus 弃用警告。
- 小型脚本、构建日志及浏览器结果位于系统临时目录 `fullnet-vform3-renderer-failure-20261024`；关闭测试浏览器与 Vite 服务，检查当前四文件 UTF-8/文档链接及 Git 空白。之前被策略拒绝的清理未重试。本轮未提交、推送或扩展业务能力，独立本地差异审查仍未完成。

## 继续复查：iframe 文档重载不能覆盖已保存设计

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，快照 `vform3-frame-reload-20261024`。本轮修改通用宿主、帧客户端、直接宿主回归及计划/记录；不修改上游、隔离运行包、API 或数据库。

原帧客户端只在第一次 Ready 时核验摘要。文档重载保持同一个 WindowProxy，而第二次 Ready 被忽略，旧 ready 状态和未完成请求仍有效；重载后的空设计 JSON 因此可能成为一次成功保存读取。原生 iframe error 也仅向外发提示，没有拒绝通信中的请求。

当前通用宿主区分首次 load 和后续文档 load；后续 load 或原生加载错误立即使会话失效。帧客户端也拒绝已就绪会话的再次 Ready，包括摘要相同或不同的情况，防止只依赖某一种浏览器事件顺序。失效后清理定时器、拒绝全部未完成请求、停止接收回执/变更，并拒绝后续请求；再次 Ready 不能恢复同一旧会话。关闭重开可建立新宿主，重新读取已保存设计或当前授权投影。首次摘要未通过时的既有校验行为保留。

这项保护保证空画布不覆盖已保存资产；它不能恢复浏览器重载已经丢弃的未保存内存编辑，不声称重新打开能还原这部分内容。正式 Renderer 同样依赖宿主错误使办理失效。

针对性证据：

- `pnpm --filter @fullnet/admin-form-designer test -- src/VForm3DesignerHost.test.ts`：首次两个原生事件用例未把 iframe 挂入 document，contentWindow 为空，属于夹具错误；改为 attachTo document 后，修复前 **4 失败、11 通过**，四个故障场景均观察到空 JSON 被错误当作成功回执。修复后最终 **15/15 通过，退出码 0**，包括同摘要/不同摘要重复握手、load/error、未完成请求、后续消息、新请求和定时器清理。
- `pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts src/workflow/VForm3RuntimeRenderer.test.ts src/views/WorkflowFormsView.test.ts src/views/WorkflowTodosView.test.ts`：**77/77 通过，退出码 0**，范围为共享宿主的两个实际包装器及直接页面。
- `pnpm --filter @fullnet/admin-form-designer build` 首次发现新增测试从 attributes 读取的 src 类型可为 undefined；改为使用 HTMLIFrameElement.src 后，宿主目标测试重跑 15/15、包类型检查通过。`pnpm --filter @fullnet/admin build` 的类型检查及生产构建通过；其生产源码未再修改，无需重复构建。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，内层摘要保持 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。
- Playwright CLI 独立 Edge 会话 `vform3-frame-reload-20261024`。运行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-frame-reload-20261024 run-code --filename reload-design-browser.js`：真实 Vue 表单页/原版 iframe，HTTP 响应受控。夹具延迟实际 getFormJson 回执后，执行真实 location.reload；保存读取立即失败，模拟权威修订仍为 2，无 PUT。再通过重载后的原始帧原生 postMessage 释放旧回执，仍无 PUT；点击保存也不能提交空画布。关闭重开后标签、根扩展、表单自定义配置/labelWidth/事件和组件扩展保持，正常保存恰好一次，模拟修订升至 3；空闲时再次实际重载并点击保存，没有第二次写入。综合场景通过，pageerror=0。
- 同会话运行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-frame-reload-20261024 run-code --filename reload-todo-browser.js`：真实 Vue 审批页/路由/原版 iframe 实际重载，等待第二次原生 Ready 后，办理仍禁用、帧隐藏且 inert、有可读故障提示、无审批写入。关闭重开后，原版输入及意见进入正常审批请求并关闭详情，综合场景通过，pageerror=0。
- 两项浏览器验证的 HTTP/权威状态为夹具，不代表新的数据库、登录或租户协议验收；此处文档重载是真实浏览器行为，延迟回执是受控注入。控制台仍有开发通道网络错误及原版 Element Plus 弃用警告，不宣称控制台无告警。后端、SQL 及运行包未变，复用前文双库/Native 证据，未扩大为全仓测试。
- 小型浏览器脚本、结果与构建日志位于系统临时目录 `fullnet-vform3-frame-reload-20261024`。收尾关闭测试浏览器及 Vite，检查五文件 UTF-8/文档链接、Git 空白、分支及既有暂存来源文件；此前清理拒绝未重试。未提交、推送或扩展后续二开，独立本地差异审查仍未满足。

## 继续复查：非法字段通知不能留下可提交的旧补丁

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，快照 `vform3-invalid-change-20261024`。本轮仅修改帧客户端、宿主回归和计划/记录；上游、运行包、依赖及后端/SQL 不变。

原帧客户端安全丢弃了无法编码、超限或结构错误的 changed 通知，却没有向调用方报告字段状态已经不可信。若上一条合法变更已形成 Patch，审批页仍保持就绪，用户的新输入可能被静默漏掉后继续提交旧值。本轮将这个可观察的数据丢失风险作为失败边界，而不是只要求消息处理器不抛出全局异常。

当前处理：只有窗口、opaque Origin、通道及 Ready 均已验证的当前帧，才可能触发业务故障。其 changed 通知必须是普通 JSON 对象且符合字段/信封大小限制；循环引用、BigInt、超限、null、数组、字符串或 Date 根对象立即使会话失效，拒绝未完成调用、清理定时器，并通过宿主错误接续清空 Patch、隐藏/禁用 Renderer 和禁止办理。原帧随后发送合法数据也不能恢复会话，关闭重开取得当前授权数据后再办理。错误来源/通道仍直接忽略，不能借非法通知打断合法会话；普通错误 RPC 回执继续仅拒绝关联请求，保留既有读取错误行为。

针对性证据：

- 将原先“非法通知忽略后继续处理”的三项回归扩为八类明确故障，并新增错误来源不影响会话的边界；保留全局无异常、合法输入、未完成调用、后续消息与定时器检查，不删除历史验证事实。`pnpm --filter @fullnet/admin-form-designer test -- src/VForm3DesignerHost.test.ts` 修复前 **8 失败、13 通过**，修复后 **21/21 通过，退出码 0**。此前协议接收章节的“后续合法变更仍可处理”是当时的历史行为，本轮明确收紧当前帧非法业务通知的恢复策略。
- `pnpm --filter @fullnet/admin test -- src/workflow/VForm3WorkflowDesigner.test.ts src/workflow/VForm3RuntimeRenderer.test.ts src/views/WorkflowTodosView.test.ts`：**56/56 通过，退出码 0**，覆盖共享宿主的实际包装器与受影响办理页面；未重复其它页面或全仓测试。
- `pnpm --filter @fullnet/admin-form-designer build` 及 `pnpm --filter @fullnet/admin build` 均通过，后者包含类型检查与生产构建。`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过；内层摘要保持 `3e59189c2b5ce69a731becee48dad422c23c3420c25514e56143b0037e8ba4c1`。后端和运行包输入未修改，复用已有双库/Native 证据。
- Playwright CLI 独立 Edge 会话 `vform3-invalid-change-20261024`，执行 `npx --yes --package @playwright/cli playwright-cli -s=vform3-invalid-change-20261024 run-code --filename invalid-change-browser.js`。真实 Vue 审批页/路由及原版 opaque iframe，每类场景先用原版输入生成合法变更，再通过原始帧原生 postMessage 注入一种非法通知。**八类均中止办理**：按钮禁用、帧隐藏且 inert、有可读故障提示，没有审批请求；原帧继续发合法消息仍不恢复。错误通道通知不影响首次实际输入。最后关闭重开，正常填写恰好产生一次审批请求，Patch 为新输入“重开后确认提交的新值”，未提交任一故障前旧值。综合场景通过，pageerror=0。
- 浏览器消息故障为夹具注入，HTTP 为受控响应，不冒充真实数据库/身份协议验收。循环引用和 BigInt 通过原生 structured clone 进入宿主，未修改运行包或伪造接收方 source/origin。控制台仍有开发通道网络错误及原版 Element Plus 弃用警告，不宣称控制台无告警。
- 小型脚本、结果与构建日志位于系统临时目录 `fullnet-vform3-invalid-change-20261024`。收尾关闭浏览器及 Vite，检查四文件 UTF-8/文档链接、Git 空白、分支和既有暂存来源；此前清理拒绝未重试。未提交、推送或扩展后二开，独立本地差异审查仍未完成。

## 最终独立审查与真实栈复验（2026-10-24）

入口仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`，快照 `vform3-final-review-realstack-20261024`。用户明确授权独立只读审查子代理，本次实际审查本地迁入差异；此前各节“独立审查未完成”为当时状态，现已补齐源码审查及修复复审。审查者没有改文件或代跑测试。

独立审查发现并修复两项 P2 问题：

1. 原版时间控件固定输出 `HH:mm:ss`，服务端原来仅接受 `HH:mm`，导致合法时间字段能发布却不能正常提交。统一时间解析接受分钟与秒精度，范围比较保留秒；VForm 入帧将分钟值补 `:00`，出帧仍为原版秒值。Patch 与归一化后的原值比较，未编辑的分钟值不会产生无意义写入。
2. 原版 `onFormMounted` 设置的字段值先于 render 回执发出 changed，原父层加载门禁将其丢弃，显示值和实际审批 Patch 因而不一致。当前 render 在原版挂载完成后通过 `getFormData(false)` 无校验读取实际模型并随回执返回；父层仅对当前代次按绑定值协议和 `editable`/`required` 白名单形成 Patch。保留旧通知的加载门禁，非法回执失败关闭，空必填项不阻断进入表单。只读/隐藏值仍不能提交。

独立复审确认两项源码问题闭合，未发现新的可执行问题；它覆盖消息生命周期、无损资产与发布快照、字段授权和值协议、双库迁移及 JSON/AOT 注册，没有逐行复核全部 338 个上游文件，也不替代下述运行证据。上游逐字节清单继续独立核验。

当前定向证据：

- 前端新增回归在修复前为 **3 失败、15 通过**；补上非法初始化回执边界后，`pnpm --filter @fullnet/admin test -- src/workflow/vform3-values.test.ts src/workflow/VForm3RuntimeRenderer.test.ts src/workflow/VForm3WorkflowDesigner.test.ts src/views/WorkflowTodosView.test.ts src/views/WorkflowFormsView.test.ts` 最终 **90/90 通过**。宿主协议目标文件 **21/21 通过**，包类型检查通过。
- 服务端范围为 `WorkflowFormValueValidatorTests`、`WorkflowDesignerAssetTests`、`WorkflowExclusiveGatewayConfigurationTests`，聚焦 **69/69 通过、0 跳过**；新增时间合法精度及秒边界用例。统一构建入口两次因并发工作区输入变化拒绝登记/自动执行，不能算作完整验收通过；实际复用刚生成的 Release，经 `node scripts/testing/run-dotnet-test-suite.mjs unit --no-build --filter 'FullyQualifiedName~WorkflowFormValueValidatorTests|FullyQualifiedName~WorkflowDesignerAssetTests|FullyQualifiedName~WorkflowExclusiveGatewayConfigurationTests' --minimum-expected-tests 29` 执行上述结果，不宣称建立了全仓可复用构建记录。此前单独十项执行均通过，没有取得服务端 RED，不记为 RED。
- `pnpm --filter @fullnet/admin build` 类型检查与生产构建通过；`node --test packages/admin-form-designer/tests/source-inventory.test.mjs packages/admin-form-designer/tests/runtime-release.test.mjs` **2/2 通过**。实际运行包更新为 `7ca67d3198b8d7ddf9aff977f3fe94c8327fb1158194bf995610b10d15bfb6e3`，`node scripts/workflow/vform-runtime-release.mjs --verify ui/admin/dist/vform3` 通过，前端常量、清单及服务端嵌入清单同步更新。历史 `3e59189c…` 产物结论保留为历史事实。

最终真实栈：

- 复用原 `workflow-forms.spec.mjs` 的严格 CSP VForm 用例，补上挂载脚本必填初值、分钟时间回显及秒时间提交断言；没有新增规格文件或模拟 API 路由。先用 `--list` 确认只发现 1 项，再通过任务临时配置运行同一文件和精确标题筛选、`vue-admin`、单 Worker。临时入口 `node .../fullnet-vform3-final-review-20261024/run-real-stack.mjs` 使用现行 `withTestRun` 重型资源门禁，并按既有独立 build 输出归属加锁。
- 生产 Vite preview `http://localhost:15173`，父 CSP 为 `script-src 'self'; object-src 'none'; base-uri 'self'; frame-src 'self'`；独立 API `15489`、真实 SQL Server 2022 CU14、Redis 及独立 Worker，Migrator 完成真实迁移及 Development seed。复用既有隔离编译目录，不复制仓库；Migrator/API/Worker 本轮构建均 0 警告/错误。此为本地开发配置验收，不代表生产部署或容量验证。
- 实际登录、创建草稿、API 写入完整原稿、页面重开、原版设计器读取/保存、发布并读取冻结快照后，创建实例并在原版 Renderer 办理。断言 `08:30` 回显为 `08:30:00`；未手动编辑的挂载脚本字段成为必填 Patch；实际输入的 `12:45:37` 保留秒。审批请求同时包含标题、挂载初值和秒值，完成后再经权威 runtime GET 读取三个持久化值一致；原稿组件数组与表单配置、冻结 JSON 均保持等价，指定 CSP/组件/pageerror 收集为空。
- 最终 **1/1 通过、0 跳过、0 flaky、0 unexpected**，退出码 0；用例本体 **13.754 秒**，包括栈构建启动和收尾的总耗时 **734.664 秒**。原始结果 `real-stack-result.json`。早期临时入口曾因 Windows ESM 路径写法和一次端口可用性检查失败而未执行用例，修正/复核后才取得本轮通过结果；排队尝试也不计为通过。
- 验收前后 817 项相关输入一致，SHA-256 `e4b0b7d3cabf957e14f95a99fb4d26fee7dae3d188dda0fb4f34875f5decbf5a`，清单 `acceptance-inputs.json`。专属 API/Worker/preview 与容器均已停止，`.stack-state.json` 已移除，原有三个共享开发容器保留。

当前原生复验的实际结果及跨窗口输入变化见下节，不能计为通过。任务脚本、输入清单及最小日志位于系统临时目录 `fullnet-vform3-final-review-20261024`；未复制整仓，未重试此前被策略拒绝的清理。

### 跨窗口后的当前输入

用户更新仓库规则后，按新 [AI 工作流](../development/ai-workflow.md) 执行 `node scripts/doctor.mjs --profile full --json`，所有检查通过；它只证明本机工具、依赖、浏览器文件及 Docker 环境可用。核对发现早先排队的原生进程已经退出且未发布，专属容器不存在；仅清除本任务 PID/token 对应的遗留工作区锁，没有清理其他任务的锁或容器。

共享工作区期间，其他任务修改了 Workflow 内核、待办页面、表单并发请求及生成契约。因此前述上午 817 项冻结输入的真实栈 1/1 保留为该输入的历史结果，原始文件已另存 `real-stack-result-morning.json`、`real-stack-morning.log`、`acceptance-inputs-morning.json`；不直接计作下午当前输入通过。新增任务快照 `vform3-final-review-current-20261024`，保留所有并行改动，并重新冻结相关代码及实际测试入口。

受影响边界的调整与验证：

- 新的审批/退回请求要求独立 `expectedFormRevision`。页面已正确使用 runtime 的 `submissionRevision`；同步 Todo 页两处旧断言、退回 API 测试，以及 VForm 托管/Native 夹具，不把 todo 修订冒充表单修订。首轮前端 2 失败、88 通过为旧断言所致。
- 任意 `JsonElement` 生成类型变为 `unknown` 后，Forms/Todos 调用边界暴露类型错误。沿用并行修订中的对象收窄，并将完整资产 GET/PUT 的校验收紧到对象根、`widgetList` 数组及 `formConfig` 对象；直接返回完整 JSON，不裁剪未知组件和扩展。Todo guard 拒绝非空非对象 Web 投影，保留 null/缺省的程序化表单接续。来源目录及运行包输入未改变。
- 新增资产坏形状回归修复前 **6 失败、3 通过**；Todo Web 投影新回归修复前 **4 失败、1 通过、16 未选中**，未选中不算通过。并行修改一度产生重复导入/mock 与重复参数，复读合并后保留双方有效覆盖，补上合法设计夹具的 formConfig；这些解析/夹具失败均不计为业务通过。
- `pnpm --filter @fullnet/admin test -- src/api/workflow-forms.test.ts src/api/workflow-todos.test.ts src/workflow/vform3-values.test.ts src/workflow/VForm3RuntimeRenderer.test.ts src/workflow/VForm3WorkflowDesigner.test.ts src/views/WorkflowTodosView.test.ts src/views/WorkflowFormsView.test.ts` 最终 **103/103 通过**；`pnpm --filter @fullnet/client-contracts test -- tests/workflow-todos.test.ts` **21/21 通过**。`pnpm --filter @fullnet/admin build` 在修复合并后通过，实际运行包仍为 `7ca67d31…bfb6e3` 且已核验。没有重跑未变化的全部原版浏览器矩阵或全仓测试。
- 原独立只读审查者再次核对当前 Todo 并发调用、VForm 双库夹具，以及两个 JSON 对象边界，确认此前时间/挂载初值修复仍闭合，新增边界未发现可执行问题；没有扩审整个新节点内核或代跑测试。

当前复验状态：

- Linux SDK 镜像仍为 `fullnet-ai-native-sdk-402:bc74b9f1dc`，镜像 SHA-256 `4488e0c3b77341253b127f67fcc8f7940caf19c1c8b69a86d8b5c5c6bea36dc1`；SDK 10.0.402、目标 10.0.13、linux-x64、2 CPU，Docker VM 19.29 GiB。源码只读挂载，原生/JIT 图分开，输出在本任务系统临时目录，没有复制仓库。
- 本次发布前冻结 4,482 项服务端与测试输入，摘要 `f76a87c887208dea46e2e4953c56aad464d882b91b4a2f618f4981fb4247298d`。Host.Api 编译后进入机器码生成；期间再次核对发现 Workflow 执行器、编译器、并行汇合、运行计划、分支 SQL 等生产源码及测试地图变化，不能将该发布记为当前输入通过。记录当时已识别的九条既有第三方 IL 告警，但没有执行发布完成门禁，不宣称原生发布成功或无告警。
- 为停止依赖失效输入的验收，主动执行 `docker stop --time 10 fullnet-vform-final-native-publish-20261024`；容器退出 **137**，原因是本任务主动停止及超时终止，不记为 OOM 或业务测试失败。没有执行后续 VForm Native 2 项及托管双库 2 项，没有生成可登记的新 ELF 验收证据。专属容器退出、重型资源锁释放。保留最小原始日志和可复用的任务编译缓存，等待输入稳定后继续，不创建新源码副本。
- 当前真实栈复验只进入资源排队，随后取消排队进程，并在核对 PID/token 后释放本任务独立输出锁；未重新启动 API/Worker/数据库，不把排队计为执行或通过。上午真实栈的已通过结果继续仅适用于其冻结输入。
- 独立审查、两项原始缺陷修复、客户端对象边界、103 项前端及 21 项 Todo 契约检查和生产构建已完成；**当前共享源码的最终 Native/双库及真实栈复验尚未完成**。已向用户请求授权与《继续完成 Workflow 模块任务》协调源码冻结窗口及验收后的恢复通知；线程消息工具要求明确授权，未获答复前没有向该会话发消息。

### 获授权后的共享输入冻结复验

用户随后明确授权联系《继续完成 Workflow 模块任务》协调冻结和恢复；该会话确认无未完成原子修改，暂缓相关源码。初次恢复发布仍受另一共享依赖的并行编辑影响：Messaging 的几个新文件在各项目编译期间陆续创建，Linux 构建报 CS2001/缺类型；随后 Windows Migrator 构建报 `DeliveryRollbackRecoveryService.cs:19` 缺 `ICommandTransaction` 命名空间。两轮均停在构建前置，不能计作 VForm 业务失败或原生/浏览器验收通过。

用户再明确授权联系《审查 Messaging 消息运维模块》协调冻结和恢复。相关编译阻塞由该会话修复后，它确认共享代码已落盘、没有本任务构建或测试进程，并暂缓后续实现。两会话的其它未完成业务不纳入本次 VForm 功能验收，也没有为本任务回退或替换其实现。

- 新快照 `vform3-coordinated-final-20261024`，分支/HEAD 仍为 `main@a77902afd87af1662af3b43206d85160eeec566b`；前端/工作流及测试入口当前冻结 1,008 项，摘要 `339f930d7e8234eb256d7184d0d87564f82981350bfd3ef777897e0cbea32652`。原生另记录实际服务端传递依赖清单，发布及测试前后均核对，ELF 本体在执行前后核验 SHA-256。
- 最新首次 Vitest 启动在执行用例前出现 7 个 worker 超时、0 项执行；doctor full 自检通过，最小 Node fork IPC 正常，单 forks worker 8/8 及单 threads worker 8/8 通过。随后仅调整本次运行的 pool/并行数，不改测试断言或仓库配置：同七个管理端目标文件使用 `--pool=threads --maxWorkers=1`，**104/104 通过**；Todo 契约目标文件使用同参数，**27/27 通过**。相比下午新增的动作/签名相关接续仍由既有调用页测试覆盖，不扩跑其它页面。
- 当前 `pnpm --filter @fullnet/admin build` 类型检查及生产构建通过；存在分块大于 500 kB 的构建提示，不报告无告警。实际运行包仍为 `7ca67d3198b8d7ddf9aff977f3fe94c8327fb1158194bf995610b10d15bfb6e3`，部署核验通过。前端生产输入未变化的既有原版清单/控件矩阵结果继续复用。
- 编译与运行验收在安全边界后拆分：任务入口以独立输出取得构建名额，Native 使用 4 CPU/8 GiB 预算、Docker CPU 配额 4，Windows 真实栈预构建使用 2 CPU/4 GiB；原生与 JIT 图分离，三宿主使用既有独立输出目录。编译完成释放构建名额后才申请重型资源运行数据库/浏览器。后续 bootstrap 再次执行同输入宿主构建，实际触发部分依赖重编译，增加了耗时，不能报告为纯增量复用；本轮未中途修改正在运行的冻结入口。未关闭模块、改变生产编译选项或增加告警豁免。

当前冻结输入的 Linux Native AOT 发布已成功，SDK 10.0.402、目标运行时 10.0.13、linux-x64，使用上述镜像与 4 CPU 配额。既有第三方 IL 告警共 9 条，经现行 allowlist 门禁通过，没有新增豁免；不报告零告警。集成驱动及 Migrator 的独立 JIT 构建均为 0 警告、0 错误。

- 服务端、契约及测试传递输入共 4,509 项，SHA-256 `3c6aa5af62ebcc5cd73f368f0f2bf8f5752a8e1d730338dacf721e06041a1369`；发布后及测试前核对未变化。
- 实际 ELF 为 145,342,464 字节，SHA-256 `e9d8d4e94500a1aeadc8e70121556117a72d2554b3981b94c14f513c69cc9fb8`，不是 JIT DLL 或旧产物。VForm 运行包摘要为 `7ca67d3198b8d7ddf9aff977f3fe94c8327fb1158194bf995610b10d15bfb6e3`。
- 仅发现并运行 `FullyQualifiedName~native_artifact_preserves_full_vform_design_and_submission` 对应 SQL Server/MySQL 两项，原生进程实际完成设计稿及冻结快照、错误摘要/隐藏字段拒绝、审批 Patch 与秒级时间回读：**2/2 通过、0 失败、0 跳过**，耗时 **3 分 42.509 秒**。
- Windows 真实栈第一次排队因等待构建预算超时退出，尚未运行用例；释放前置资源后重新排队同一精确场景，没有据此修改业务断言或扩大测试范围。

同一驱动仅发现并运行 `FullyQualifiedName~WorkflowDesignerAssetApiTests` 两项，托管 SQL Server/MySQL 设计资产实际往返及办理 **2/2 通过、0 失败、0 跳过**，耗时 **3 分 43.682 秒**。原生、托管两份 TRX 均经现行覆盖校验器检查发现 UID 与执行 UID 精确相等，任务退出码 0；全部 4,509 项输入及 ELF 执行后摘要保持不变，生成 `native/acceptance-manifest.json`。随后再次核对 1,008 项前端/工作流冻结输入也未变化。

原始日志、清单及 TRX 位于系统临时目录 `fullnet-vform3-final-review-20261024`。原生专属容器与测试数据库已退出，仅剩既有共享双库容器。确认专属进程退出、证据已保留且绝对路径位于本任务 native 目录后，使用原生 PowerShell 对精确 `aot-build`、`jit-build`、`publish` 三目录执行有界递归删除；命令被 `blocked by policy` 拒绝，未执行删除，也未换解释器或变体重试。约 4,145,674,328 字节（3.86 GiB）可重建输出仍保留；这不是 Auto-review 拒绝，不影响已完成测试结果。此前其它目录清理拒绝继续不重试。

最终真实栈复验：

- 同一临时入口 `node .../fullnet-vform3-final-review-20261024/run-real-stack.mjs`，实际执行 `playwright test --config .../playwright.config.mjs workflow-forms.spec.mjs --grep '管理员可在严格 CSP 下通过 VForm3' --project vue-admin --workers 1`，仅发现和运行原有规格中的 1 项，未模拟 API。三个宿主预构建和启动入口的构建均通过，均为 0 警告/错误；Playwright 出现 NO_COLOR/FORCE_COLOR 环境提示，不宣称整个过程无告警。
- 使用生产前端 preview、Edge、严格父 CSP、独立 API `15489`/Worker、真实 SQL Server 2022 CU14 和 Redis；Windows 构建与服务设置 `DOTNET_PROCESSOR_COUNT=2`，构建资源登记 2 CPU/4 GiB；Docker VM 19.29 GiB。Migration/Development seed 真实执行，本地开发验收不外推生产部署、容量或其它数据库浏览器矩阵。
- 登录、草稿创建、完整设计资产保存重开、原版设计器再次保存、发布和冻结快照等价均通过；原版 Renderer 的挂载初值形成必填 Patch，`08:30` 显示为 `08:30:00`，实际填写 `12:45:37` 后，审批请求及完成后的权威 GET 均保留秒和初值。指定 CSP/组件错误及 pageerror 收集为空。
- **1/1 通过、0 跳过、0 flaky、0 unexpected，退出码 0**。用例本体 **13.016 秒**；Playwright 报告含 global setup/teardown 总计 **590.368 秒**，不含外层三宿主预构建。原始结果为 `real-stack-result.json`，完整日志 `real-stack-final.log`；上午旧输入的结果已单独保留，不混作本轮证据。
- 执行后两份输入清单再次核对：1,008 项摘要仍为 `339f930d…32652`，4,509 项仍为 `3c6aa5af…a1369`，均未变化。26 个直接相关文件 UTF-8、181 个本地文档链接及定向 `git diff --check` 通过；分支/HEAD 不变，六个既有暂存上游文件保持。实际 `ui/admin/dist/vform3` 再次通过运行包摘要核验，Node 有上游缺省模块类型提示，未修改原始上游文件。收尾 API PID 37624、Worker PID 8240 均已退出，`15173`/`15489` 无监听，栈状态文件已移除，专属容器已清理；既有共享双库及其它任务容器保留。

本次 VForm 独立审查及冻结输入的定向原生/托管双库、真实栈验收已完成；完整节点、其它原生路径、全部生产业务绑定及跨端等价不在本次结论范围。文档更新和收尾核验后，已按用户授权分别向《继续完成 Workflow 模块任务》《审查 Messaging 消息运维模块》发送恢复通知，两次发送均成功；明确可恢复源码编辑，并说明本次 VForm 证据不能外推到它们后续改动或整个模块。没有要求继续冻结或等待二次确认。本任务专属工作区锁、构建名额及重型锁均已释放，其它任务遗留锁未操作；未提交或推送。
