# CodeGeneration 合并审查与整改建议

- 日期：2026-10-24。
- 状态：静态审查完成；整改建议已留档，代码未整改、运行验收未执行。本次用户授权为更新相关文档，不构成实施、部署或发布授权。
- 基线：`main @ a77902afd87af1662af3b43206d85160eeec566b`，结合当时工作区源码；其他模块的未提交改动不作为 CodeGeneration 验收证据。
- 输入：用户提供的 `2026-10-15-codegeneration-module-gap-analysis.md`（原报告基线 `aa305160`）与本轮调用链复核。原报告中的操作建议仅作为评估材料；竞品比较未重新核实，不作为整改排序依据。
- 范围：CodeGeneration Host 模块、生成内核、相关 Vue 工作台、CLI 接入链、Git、Helm 与生产镜像定义。
- 方法：静态阅读实现、既有规格与测试源码；没有执行生成产物编译、双库业务测试、Native AOT 发布、浏览器验收或故障恢复实验。
- 关系：补充原报告，并纠正当前百科的能力描述；不替代历史验证事实，也不以建议稿覆盖已批准规格。后续实施涉及契约、数据结构或 Git/Apply 长期边界时，应同步修订对应现有规格。
- 方案复审：2026-10-24 按用户“审查方案，有问题直接更新”修订优先级、批次依赖与执行不变量（§4.1–4.4）；本次仍只更新方案，不授权代码实施或状态升级。
- 后续授权：同日用户明确要求“执行方案”，已转入[整改执行计划](../superpowers/plans/2026-10-24-codegeneration-remediation.md)。上文的未实施/仅文档描述保留为审查时点事实，实施状态与新鲜验证统一记入活动计划。
- 2026-10-27 最新实施回写：CG-01–12 对应整改及补验修复已落入实现，本问题表保留为审查时点记录。当前工作区已正常重建正式 Integration 工程及全部项目引用，组织测试保留，一次执行 16 项全部通过；另新增协议 HTTP 回归 3 项通过，均零跳过。重建中发现的 Identity 实验 API 编译问题已改用稳定过滤接口修复。前次冻结依赖、组织测试排除及 12＋2 项仅保留为历史，当前结论以[最新执行记录](../superpowers/plans/2026-10-24-codegeneration-remediation.md)为准，不外推全量测试、真实远程发布或生产部署。

## 1. 结论与保留边界

整改顺序为：生成代码的数据正确性与可运行性 → 工作区应用和恢复 → 单表工作台闭环 → 关系页面与受控扩展。现有路径校验、摘要、冲突检测和底层写盘恢复机制继续复用；不能由这些局部机制推导出 Host 整链接入已具备完整原子性。

保留 Dapper 显式 SQL、SQL Server/MySQL 成对实现、可信租户与精确权限、同模块事务、禁止跨模块 JOIN/外键/本地事务、Vue 唯一持续交付线等基线。目录仍只读当前配置数据库，不接受请求连接串；zip 保持鉴权下载；生成代码不在 API 中加载执行，Host 不执行运行时 DDL；迁移草案仍须正式编号和双库恢复验证。编译门指外部工具验证源码，不代表热加载或部署。

## 2. 已确认的静态证据与整改方向

下列 P1 为应优先处理的正确性、恢复或运行阻断问题，P2 为产品闭环与可维护性问题；均未在本次运行复现。源码链接指向本次基线对应实现。

| 编号 | 优先级 | 触发条件与当前行为 | 整改方向 |
| --- | --- | --- | --- |
| CG-01 | P1 | 生成删除先级联明细，再按主记录版本删除；过期版本返回失败 Result，但外围 `ExecuteAsync` 正常返回即提交，可能失败却删除明细。 | 生成写用例使用 `ExecuteResultAsync`，覆盖所有写入后返回失败的路径，保证主从原子回滚。 |
| CG-02 | P1 | 唯一性方法只按第一唯一列生成，调用点却遍历所有唯一列；不同类型可能编译失败，同类型可能查询错列。迁移草案缺少相应唯一约束。 | 按列生成校验、SQL 和索引；明确作用域、NULL、软删复用及更新排除自身语义。数据库约束负责并发唯一，预检查仅改善错误提示。 |
| CG-03 | P1 | ManyToMany 要求当前实体为两条关系的从端，生成器为两条关系输出同签名 `EnsurePrincipalExistsAsync`；关系表名按实体键推导，级联固定 `DELETE`。 | 按关系分配方法及 Statement ID，解析完整主从 Schema 的表名、关系列和删除策略；缺少必要元数据时拒绝生成，不能用主端删除模式代替从端策略。 |
| CG-04 | P1 | Host 每次只规划一个 Schema，全局清单中不在本次产物集合的其他实体文件会被判为过期；未人工修改时进入删除计划。 | 清单记录 owner/module/entity 所有权，仅清理本实体过期产物，共享文件单独管理；路径包含模块维度以防同名实体碰撞。 |
| CG-05 | P1 | 检查点在基础产物写入前建立，之后的模块入口、Composition、Vue、授权修改没有统一纳入；Vue 还会改变根清单。接入失败保留此前阶段，而产品回滚只接受成功 Apply。 | 先形成完整变更计划并验证，再保存全部前后摘要与恢复材料；记录提交阶段，支持整链恢复或继续，不能把部分失败伪装为零写入。 |
| CG-06 | P1 | push 失败只记警告，本地运行仍成功；下一次 `fetch + reset --hard` 可清除未发布的工作区结果。`add -A` 还会包含计划之外的脏文件。 | 分离本地应用状态与远端发布状态，保存提交 SHA、基线和待发布记录；先重试发布或阻断同步，不清除未发布结果。限定提交文件，采用显式工作分支，PR 后置。 |
| CG-07 | P1 | 生成 Feature 使用匿名 SQL 参数，注册入口没有生成 Record 物化器；项目 AOT 执行器拒绝未注册参数和物化器。 | 生成静态参数容器/绑定器与双库物化器，显式注册；分别验收生成器自身与生成产物的 AOT 能力。 |
| CG-08 | P1 | 写盘成功后数据库终态失败或进程退出，会遗留 `running`；回滚中断遗留记录可使后续请求持续 Busy。 | 持久化操作阶段与工作区身份；取得同一工作区锁后按文件摘要核对恢复，并隔离旧执行者，不能仅凭超时判失败。与 CG-05 同批完成。 |
| CG-09 | P1 | Preview 绑定生成摘要，没有绑定完整工作区增删改及接入目标；`IntegrationTarget` 在 Apply 基础写盘后才解析。 | 区分无工作区的产物预览与完整 Apply Plan；后者绑定目标、工作区基线、操作列表及摘要。全部目标校验先于写盘，锁内复核，漂移后重新预览。 |
| CG-10 | P2 | `queryKind`、`sortable`、`includeInImportExport` 可配置，但生成输出没有对应闭环；Vue 除 switch/textarea 外使用普通输入框，缺少表单规则与国际化。 | 查询/排序贯通契约、参数化 SQL、白名单和页面；未实现选项禁用并说明。按类型输出控件与前后端校验，复用后台布局和国际化约定。 |
| CG-11 | P2 | 历史下载按当前模板重算，直接 Schema 预览不支持下载；模板或生成器变化影响重现。 | 冻结规范 Schema、生成器版本、选项、清单摘要；可靠历史下载保存当次受控产物并设置保留期限，不能仅靠 Schema 快照重算。 |
| CG-12 | P2 | Helm `production && enabledWhenProduction` 可隐式开启 Apply；镜像定义未提供 SDK/Git 工具链，检查点共享卷不等于发布闭环。百科还写有临时分支/PR 和错误表名。 | 改为显式启用，写盘前检查所需工具；优先现有 CLI 在开发/受控构建环境验证和提交。准确描述当前行为，文件落入 PVC 不代表运行镜像更新。 |

### 2.1 关键源码证据

- CG-01：[生成事务与删除顺序](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudBackendFeatureGenerator.cs)、[ExecuteAsync / ExecuteResultAsync](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs)。验收必须包含“主记录版本冲突、明细不变”。
- CG-02/03：[守卫生成](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudSceneGuardGenerator.cs)、[关系不变量](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs)、[关系契约](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudRelationship.cs)、[双库迁移草案](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudMigrationTemplateGenerator.cs)。
- CG-04/05/09：[Host Apply](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationApplyService.cs)、[全局写计划](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationWritePlanner.cs)、[接入编排](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Integration/ModuleIntegrationHostOrchestrator.cs)、[检查点对齐](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GenerationRollbackWorkspace.cs)。
- CG-06/08：[Git 同步发布](../../src/Modules/Full.NET.Modules.CodeGeneration/Git/CodeGenerationGitWorkspaceService.cs)、[产品回滚资格与 Busy 判定](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationRollbackService.cs)。历史 Git 方案见[原规格](../superpowers/specs/2026-08-02-codegeneration-remote-git-design.md)，修复时需同步修改其失败语义。
- CG-07：[生成 Feature](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudBackendFeatureGenerator.cs)、[AOT 参数边界](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs)、[AOT 物化器边界](../../src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotSqlExecution.cs)。
- CG-10/11：[列 UI 元数据](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetColumnUi.cs)、[Vue 生成](../../src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudVueViewGenerator.cs)、[历史下载](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationArtifactDownloadService.cs)。
- CG-12：[Helm 默认值](../../deploy/helm/fullnet/values.yaml)、[实际启用条件](../../deploy/helm/fullnet/templates/_helpers.tpl)、[镜像定义](../../eng/docker/Dockerfile)、[启动期工作区校验](../../src/Modules/Full.NET.Modules.CodeGeneration/Configuration/CodeGenerationApplyOptionsValidator.cs)。镜像能力来自定义审查，本次未启动镜像实测。

## 3. 原报告结论的修订

1. “单实体已经包事务”是结构事实，不证明失败会回滚；CG-01 是必须补充的失败语义缺陷。
2. “安全写盘、检查点、冲突零写入”只能在对应底层计划/阶段边界成立，不能概括 Host 完整接入链。已有阶段提交后失败，不等于工作区未变化。
3. 多唯一列、多对多、AOT 缺口仍存在。Tree 有父节点/环检测，但页面仍是平表；不能仅凭守卫文本宣称并发修改场景已验证。
4. Schema 模板不是可执行或可替换代码模板；受控模板包暂后置，不预先选用 Scriban/Liquid，也不新增运行时脚本能力。
5. 历史下载需冻结生成器版本及必要产物；发布失败应独立表达，不能把“本地已应用、远端失败”压成单一失败状态。
6. `.template` 迁移、只读目录、Host 权限、禁止跨模块数据库访问、Layui 冻结属于保留边界，不以竞品对标为由取消。
7. 不通过为生产业务 API 扩充 SDK/Git 来默认承担生成构建发布；先使用已有 CLI，是否需要独立执行设施另按真实消费者证据评估。

## 4. 建议实施批次与验收场景

下表是后续整改顺序与验收目标，不是已完成清单。文档纠偏随对应批次更新；同一功能批次完成后集中验证，只运行实际变更及直接必要边界，不自动运行整模块或全仓测试。

| 批次 | 范围 | 必要验收场景 |
| --- | --- | --- |
| 一：生成正确性 | CG-01/02/03/07 及 CG-10 服务端写入校验；冻结实体/关系输入、失败事务、唯一约束、关系代码、AOT | 双库主版本冲突后明细不变；string/int 两唯一列可编译、各查本列、并发重复被约束拒绝；ManyToMany 两关系可编译且查各自显式表、重复关联被约束拒绝；从端软删/不可变语义正确；Required 空值和 decimal 越界稳定失败；代表实体原生 CRUD 覆盖实际改变的参数和物化路径。 |
| 二：安全应用与恢复 | CG-04/05/06/08/09 及 CG-12 执行边界，作为同一恢复闭环交付 | A→B→更新 A 不改变 B；同名跨模块不碰撞；含共享文件的 A/B 操作按工作区 LIFO 回滚；重复 Apply 不重复执行；各阶段失败及数据库断连/重启可核对恢复；旧执行者失锁后不能继续提交；目标非法/工具缺失先于 Git 或文件修改失败；push 拒绝/响应丢失后结果不丢且可重试。 |
| 三：单表工作台闭环 | CG-10 页面及查询/排序、CG-11 历史下载；复用第一批输入契约和第二批 Apply Plan | contains/equals/range 与排序仅接受声明字段，值参数化且排序含稳定唯一尾键；数字/日期序列化与契约一致，前后端校验一致；切换受支持语言无硬编码文案；Apply 禁用时也能预览/下载；直接 Schema、模板修改或删除、生成器升级后仍可下载保留期内当次 zip；到期明确提示，未实现选项不可被误认为生效。 |
| 四：关系页面与扩展 | Tree、MasterDetail、关联实体维护；按实际需求补字典/导入导出 | Tree 展示和父节点选择、API 自引用/环拒绝；主从组合写入在本模块事务内全成全败；ManyToMany 维护关联实体；导入导出复用既有模块边界。增量迁移仍为受审查草案，模板扩展以真实项目需求为前提。 |

未来批次一、二若引入数据结构或行为变更，应为对应场景成对验证 SQL Server/MySQL；Native AOT 和浏览器用例按实际变化选择。不能用字符串包含断言代替生成代码编译，用 JIT 编译代替原生运行，或用模拟单测代替双库事务恢复证据。

### 4.1 批次依赖与最小输入契约

原方案把输入冻结、写入校验和历史下载一起推迟到第三批，会使第一批关系生成继续依赖可变模板；把恢复列为 P2 也会允许第二批只交付写盘。复审后按以下依赖执行：

1. 第一批先统一规范生成输入：当前实体及所引用实体的不可变 Schema、owner/module、作用域、实际表名、关系列、删除/审计/版本能力、唯一规则及生成器版本/选项。CLI 与 Host 共用同一解析和校验内核；引用实体不在同一模块/作用域或无法解析时拒绝，不能退回拼表名。第一批不依赖历史下载存储上线。
2. 关系应校验主端被引用列的唯一性和类型，明确租户联合约束；`CompositeKeyColumnNames` 不能只作准入标记。关联实体的逻辑主键与业务复合唯一约束分别建模，不把一个 UUID `Id` 冒充关联对的唯一保证。不可变从端禁止级联修改，软删需同步该实体声明的删除审计和版本语义。
3. 唯一语义在生成输入中明确：默认非 NULL 值在声明作用域内唯一，NULL 不参与重复判定，软删记录默认继续占用唯一值；活动行复用属于显式扩展。双库约束与预检查必须一致，不能等价实现的组合在生成前给出稳定诊断。并发约束冲突须映射稳定业务错误，不能统一变成 500。大小写/归一化按明确业务规则，不能由提供程序默认行为暗中决定。
4. `Required` 字符串、长度、decimal 精度/小数位及系统字段不可写等服务端校验进入第一批；前端控件在第三批实现，但服务端正确性不能依赖页面。Int64/decimal 的前端表示先遵守现行线格式，禁止控件转换导致静默精度损失。
5. 第一批完成前，问题组合不能被标为可生成可用代码；第二批恢复闭环通过前，Host 接入和 Git 写入不能被标为安全开放。实现时可先让不支持的组合返回明确诊断，但拒绝路径不计为最终功能验收通过。第三、四批分别复用已完成的输入和应用契约，不重新实现一套生成流程。

### 4.2 工作区应用、恢复与回滚不变量

磁盘多文件、数据库和 Git 不在同一物理事务中。目标是可核对、可恢复的操作闭环，不承诺任意时刻所有观察者都看见原子切换。

- **完整计划先于变更：** 内存产物预览保持无工作区依赖；Apply Plan 单独绑定服务器管理的工作区身份、全套输入摘要、生成器版本、目标、Git 基线、所有文件前后摘要及创建/更新/删除动作。基础产物、模块内清单/注册表、入口、Composition、路由与授权均纳入。工具、路径、恢复现场和目标校验先于 Git 操作与真实源码写入；禁止 reset/强推消除本地结果，编译验证只使用按需临时投影，不复制整仓。
- **锁内复核并幂等：** 同一持久化工作区身份用于 API、CLI、回滚、恢复和清理协调，不能仅靠各实例的绝对路径哈希判断资源相同。Apply 使用持久化幂等键，同键同计划返回既有执行状态，不创建第二次运行；同键不同计划冲突。占锁后重查资格、基线和摘要，过期计划失败关闭。同步远端若改变基线，应重新生成计划，不能继续执行旧预览。
- **持久化阶段：** 至少能区分计划已准备、文件提交中、本地已应用、恢复待处理与已恢复；Git 发布状态独立保存。写盘前耐久保存恢复材料，提交中记录阶段；数据库终态失败不得删除本地日志，重启以实际摘要核对补记。恢复者必须证明旧执行者已失去写入权，超时或数据库会话锁释放本身不证明磁盘写入已停止；没有旧执行者隔离证据时停止自动恢复并保留现场，不宣称多实例安全。
- **冲突与回滚：** 首批使用工作区全局 LIFO 顺序回滚，而不是现有按 module/entity 筛出的链；共享 Composition/授权/路由会使不同实体操作相关。撤销 B 后才能撤销更早的 A。跨实体选择性回滚后置，不能通过放宽清单对齐校验实现。补偿只还原仍匹配本次期望后态的文件，遇到人工漂移保留材料并明确待处理，不覆盖人工文件。
- **证据生命周期：** 运行中、待恢复、待发布或仍被回滚链引用的材料禁止清理；清理必须与 Apply/Rollback 使用同一工作区身份和互斥边界。容量不足时在写入前拒绝新操作，不能为腾空间删恢复证据。HTTP 取消/响应丢失和本地已提交分开处理，重试通过幂等状态取得真实结果。

对应实现审查入口：[锁资源身份](../../src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/CodeGenerationWorkspaceLockResource.cs)、[当前回滚链查询](../../src/Modules/Full.NET.Modules.CodeGeneration/Persistence/CodeGenerationRunSql.cs)、[检查点清理](../../src/Modules/Full.NET.Modules.CodeGeneration/Retention/CodeGenerationCheckpointRetentionRunner.cs)。上述为整改目标，不是当前实现能力。

### 4.3 Git 基线与下载保留边界

仅把 push 改到临时分支不能解决下一次 reset 回主干丢结果的问题；仅保存 Schema 也不能保证历史产物重现。

- Git 首批使用显式配置的工作分支及持久化提交链，初始基线经计划确认；后续沿该工作分支继续，不重新硬重置到默认主干。默认分支只作为受审查的初始来源，不作为生成写入目标；禁止强推。远端移动或分叉时冲突停止，不自动合并、重置或重生成已审查内容。
- 保存本地提交 SHA 与预期远端提交。push 返回失败或响应丢失时先核实远端是否已有该提交，再重试同一提交；发布重试不重新生成文件。本地已应用和发布失败分别展示；后续写入须等待前次发布收敛或明确进入受控恢复，不暗中丢弃未发布提交。
- Git 仅提交计划允许的源码和必要生成清单；恢复正文、临时编译材料、下载包和凭据排除。使用专用工作区并拒绝计划外脏内容，不能借 `add -A` 或整份现有暂存区提交。PR 自动化继续后置。
- 下载快照有独立于 Apply 检查点的生命周期：Apply 禁用、直接 Schema 预览、模板删除以及成功回滚后，保留期内仍应下载当次产物。成功预览的摘要/收据必须关联完整持久化包，不能先报告可下载成功再补写文件；存储失败返回稳定状态，不留下伪成功下载记录。
- 复用既有受控文件边界实施鉴权、完整性、大小/容量和保留限制；下载前重新授权。到期明确返回稳定过期语义，不悄悄重算成新版本。清理采用独立保留规则，不把删除回滚检查点等同于删除下载快照，也不使本地临时构建材料长期留存。

### 4.4 必须补入的针对性验收

以下是对批次表的必要边界补充，后续只在对应实现变化时执行：

| 批次 | 原方案遗漏的验收边界 |
| --- | --- |
| 一 | 同类型与异类型多唯一列、NULL/软删/租户隔离及并发重复；关系快照中的受控表名、物理/CLR 列名与主端列（不绕过 owner/module/entity 命名约束）；重复关联约束；不可变从端；失败 Result 在最外层与已有事务内均阻止错误提交；测试调用真实生成代码，不只手写等价逻辑。 |
| 二 | A/B 跨实体共享入口后按全局顺序回滚；相同工作区不同实例挂载路径；失锁旧执行者与恢复者竞争；每个提交阶段进程终止、人工漂移；Apply 与清理并发；同键重试、HTTP 取消及成功响应丢失；远端已接收但本地收到失败的重试；Git 计划外暂存文件；不支持工具时 Git 与业务文件都零修改。 |
| 三 | Apply 关闭的直接预览下载；模板删除及检查点清理不影响仍在保留期的包；下载存储失败、过期、权限撤销与容量拒绝；非唯一排序追加唯一尾键；数字精度、日期和 NULL 的往返。 |
| 四 | Tree 两个并发请求互设父节点，不能均提交形成环；主从更新失败、删除策略及版本冲突全回滚；关联实体授权与重复提交。普通事务和顺序祖先检查不能单独证明并发无环，实施时需选择并验证同模块串行化策略。 |

公共契约或数据结构采用当前开发底座直接演进规则，同批更新调用方、生成器、测试和文档；不为本次整改额外建立历史兼容层。当前建议稿仍不替代已批准规格，实际实施涉及的 Apply、回滚链、Git 和生成契约规格应同步更新，不能只改本审查记录。

## 5. 状态与未验证项

历史 `Build-verified` 仅保留原证据覆盖范围，不表示以上缺口已关闭。当前不能据此声明完整业务脚手架、整链原子回滚、生成产物 Native AOT 或生产构建发布闭环已验收。

本次文档修订只做文档结构、链接、编码与差异检查；上述运行场景全部待实际整改时聚焦验证。总体能力不升级为 `Verified`、`Production-verified`，容量维持 `Capacity-not-verified`。原报告中竞品能力和相对优劣未经本轮外部核实。
