# 客户端依赖审计修复验证

最新续办批次已完成：原 9 Low / 14 Moderate 全部修复，官方审计五级均为 0，Low 门槛且无例外。Vue / uni-app / 共享包 1748 项单测及必要构建、浏览器、工具链验证通过；冻结 Layui 的既有退役债务和临时目录清理策略拒绝单独记录在后半部分。前半部分保留第一批 Vite 修复的历史证据。

日期：2026-10-20（Asia/Shanghai）。代码基线为 main / `49345c3c9823360071433adb4928e09691a35d62`；任务快照 `vite-audit-remediation-20261020`。本批按用户“不延期安全例外、直接修复并集中验证”授权执行，保留此前所有安全、分层和性能任务的工作区修改，未提交、推送或部署。

## 根因与修复

最小复现 `pnpm audit:clients` 返回 1：Vite 例外 GHSA-fx2h-pf6j-xcff 于 2026-10-18 到期。npm 官方原始报告另有 5 High / 3 Critical；旧门禁在首项失败时退出，后续阻断尚未显示。

- uni-app Vite 5.4.21 升至 6.4.4；DCloud 主插件保持同一发行，legacy / JSX 插件升级为支持 Vite 6 的 6.1.1 / 4.2.0。DCloud 主插件仍声明精确 Vite 5.2.8 peer，根配置仅精确接纳经本项目验证的 6.4.4，不宣称上游正式支持。这不是安全审计例外。
- 工作区 Vitest 升至 4.1.11，移除旧测试池 Critical 依赖链。Vue / SSR 升至 3.5.42，并成对更新实际消费者的 Vue 版本；source-map-js、proxy-addr、brace-expansion 分别固定 1.2.2、2.0.8、2.1.7。
- braces 所有官方发行仍在 GHSA-vfj7-8cjw-p6xm 影响范围，核查时无修复版。制作明确私有的 `@fullnet/braces@0.1.0`，根 override 将 chokidar / micromatch 等实际链全部指向它。来源为官方 3.0.3 tarball，验证 SHA-512，保留 MIT 原许可和来源提交；没有改版本冒充上游安全发行。仅添加固定解析栈 / AST 深度防护、严格树与字段校验、受控拒绝循环 / 共享引用，并在未闭合解析回退时同步父项，保留浅层字面量结果。
- 生产 `security/client-audit-policy.json` 的 exceptions 清空；不修改原审计器、官方 registry、Critical/High 门槛或失败关闭规则。旧例外仅作为独立测试夹具，持续检查过期、越路径、包名不符、Critical 等拒绝行为。`test:workspace` 入口纳入安全与审计策略回归，现有 CI 调用此入口即可运行。
- DCloud 小程序内置 CSS 插件只按 sass 包名加载。首轮小程序构建暴露依赖缺失后，为该包名显式配置 `npm:sass-embedded@1.100.0`，与 H5 使用同一实现；不添加不同 Sass 运行时。THIRD-PARTY-NOTICES 同步版本及私有分支说明。

参考：[Vite 官方安全公告](https://github.com/vitejs/vite/security/advisories/GHSA-fx2h-pf6j-xcff)、[braces 公告与未修复状态](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)、[Vue 官方 SSR 公告](https://github.com/vuejs/core/security/advisories/GHSA-g2v6-rqmx-r4w6)。DCloud / 插件的实际 peer、版本和 MIT / Apache-2.0 元数据从 npm 官方 registry 读取；本地分支详细边界见 `packages/braces/README.md`。

## 环境与集中验证

Windows 10 Pro 19045，i7-12700H、64 GiB RAM；Node 24.12.0、pnpm 10.26.0；浏览器使用系统 Edge。没有复制整仓、运行无关后端构建或修改服务端行为。

| 检查 | 实际结果 |
| --- | --- |
| `pnpm test:workspace` | 25/25，0 跳过；安全包 15、审计策略 9、工作区 1 |
| 上游正常输入对照 | 官方 tarball 验证完整性后，同一进程比较 compile / expand / stringify；32 个模式、96 次结果一致 |
| uni-app / shared Unit | uni-app 144、client-contracts 290、admin-i18n 13、admin-form-designer 8，全部通过，0 跳过 |
| Vue 完整 Unit | 285 文件、1293/1293，通过，0 跳过；541.61s |
| uni-app 类型检查 | 通过 |
| uni-app 三端构建 | H5、微信、支付宝均通过；两个小程序在 Sass 名称修正后重跑 |
| Vue 类型检查与生产构建 | 通过 |
| H5 浏览器 | 7/7，0 跳过、0 flaky；最终依赖重建 H5 后启动本地开发服务器，完成真实 Edge UI / 本地 API 夹具验证及生产产物 DEV bridge 排除检查 |
| 包体预算 | 3/3；初始静态图 69 chunk，minified 1,494,792 bytes（+4.79%）、gzip 389,555（+3.35%），未提高预算 |
| 治理 / Integration tooling | 85/85、153/153，通过 |
| 冻结安装 | pnpm install --frozen-lockfile 成功，resolution skipped；Vue admin 实际依赖身份保持不变 |
| 审计与许可证 | 冻结安装后 gate 返回 0，exceptions=[]；raw High=0 / Critical=0，许可证枚举返回 0 |

纯客户端变更按开发质量 §11.1 不要求双库或 Native。affected-plan 为 tooling，因 root package.json 命中 Integration 工具链，已运行该 Node 组；没有将这一结果称为后端全项目回归。H5 浏览器使用 mock API / 应用夹具，不冒充真实后端认证或设备端验收；微信 / 支付宝只声明构建通过，未执行开发者工具或真机验收。

Vue 完整回归使用 `pnpm --filter @fullnet/admin exec vitest run --maxWorkers=2 --testTimeout=15000`；其他四个消费者使用同样 Vitest 并发 / 超时设置。小程序补 Sass 别名时仅安装 uni-app 及其 workspace 依赖，并比对 Vue admin 的 Vitest、Vite、Vue、test-utils 实际解析路径和包摘要均未变化，未将安装动作等同于共享依赖升级。H5 重建在构建名额下执行，随后通过 `enterHeavyTestPhase` 转入浏览器验收，未复用已有开发服务器。

## 缺陷回归与独立复核

原 braces 外部深层 AST 可导致原生 RangeError；回归证明受控拒绝。在早期测试中字符串夹具超过上游 10,000 字符预算，已缩至预算内的 6,001 字符以区分深度约束。独立只读复核发现并关闭了 parent 环、非数组迭代器、重复子树指数遍历和循环数组 value 边界；所有恶意 AST 场景使用带 2 秒超时的子进程，避免回归自身挂死。严格 parent 校验暴露的正常未闭合解析回退也先失败再修正，六种浅层字面量回归均通过。

最终审计仍有 9 Low / 14 Moderate，按原有 High 门槛不阻断；未降低门槛、忽略公告或声称全部级别漏洞清零。安装保留 pnpm 对 esbuild / vue-demi 构建脚本的既有拒绝执行提示，没有放宽脚本信任配置；规定构建与浏览器均实测通过。

独立复核已确认所有上述阻断关闭、精确别名替换及 MIT 许可完整；没有把自检当成独立审查。上游未修复的私有分支仍需持续跟踪新公告，不能仅依赖 npm 对包名的识别；这是明确维护边界，不是安全例外或“永远安全”的声明。

## 原始记录

记录位于本机系统临时目录 `C:/Users/Administrator/AppData/Local/Temp`，ADMINI~1 为同一目录的短路径：

- 原始失败：`fullnet-vite-audit-before.log`、`fullnet-vite-audit-raw-before.json`、`fullnet-vite-braces-red.log`、`fullnet-vite-iterable-red.log`、`fullnet-vite-fallback-red.log`、`fullnet-vite-mp-weixin.log`。
- Node：`fullnet-vite-workspace-final.log`、`fullnet-vite-differential.log`、`fullnet-vite-governance-final.log`、`fullnet-vite-tooling.log`。
- Unit：`fullnet-vite-admin-unit.log`、`fullnet-vite-clients-unit.log`；类型 / 构建为 `fullnet-vite-uni-types.log`、`fullnet-vite-{mp-weixin,mp-alipay}-final.log`、`fullnet-vite-admin-build.log`。
- Browser：`fullnet-vite-browser.log`、`fullnet-vite-uniapp-e2e.json`；日志含最后 H5 生产构建和 7 项浏览器结果。
- 冻结安装：`fullnet-vite-frozen-install.log`，完整性 / 实际依赖身份比对另在会话命令结果留存。
- 审计 / 许可 / 包体：`fullnet-vite-audit-final.log`、`fullnet-vite-audit-raw-final.json`、`fullnet-vite-licenses.json`、`fullnet-vite-bundle.log`。

原生二进制缓存属于前批，本批未删除或改动。前端继续复用既有构建目录，没有创建根 artifacts 或整仓副本；临时对照脚本含可重生成上游源码，完成后按精确文件清理，仅保留结果日志。冻结安装后重跑工作区 25 项、治理 85 项、审计与许可证均通过；git diff --check 无空白错误，分支 / HEAD 未变，保留既有无关修改。H5 开发服务器 5175 已退出；可重生成的临时上游对照源码脚本已按精确绝对文件路径用原生 PowerShell 删除，只留结果日志。

## 后续批次：9 Low / 14 Moderate 全部修复

沿用相同 main / HEAD，新增任务快照 `client-audit-all-severities-20261020`。前述 9 Low / 14 Moderate 是第一批交付时的历史结果；本批用户明确要求继续清零，未新增或延期任何审计例外。

### 依赖与消费边界

官方初始报告包含 19 条公告、23 个受影响版本组，不能把公告条数误当作漏洞元数据计数。实际修复如下，所有版本均固定在 pnpm-lock.yaml：

| 原依赖或路径 | 最终修复 |
| --- | --- |
| Jimp 0.10.3 → phin 2.9.3 | DCloud 微信拥有者依赖直接升级 Jimp 1.6.1；精确 pnpm 补丁将唯一 `require("jimp").read(t)` 改为 `.Jimp.read(t)` |
| DCloud esbuild 0.20.2 | 0.25.12，与已验证 Vite 6 的 native binary 一致 |
| DCloud Express → cookie / send / body-parser | Express 4.22.3，带入已修复的同主版依赖链 |
| qs 6.11.0 / 6.13.0 的多条公告 | 统一固定 6.16.1 |
| Intlify core-base 9.1.11 | DCloud core-base / shared 成对 9.14.5，运行时显式使用应用已安装的 vue-i18n 9.14.5 |
| Dockerode → uuid 10 | 11.1.1，保持实际消费者依赖的 CommonJS `v4()` API |
| Babel core 7.25.2 | 精确旧版本选择器升至 7.29.7，不干预其他主版本 |
| PostCSS 8.5.19 | 8.5.23 |
| selector-parser 6.1.2 / 7.1.4 | 7.1.6 |
| 冻结 Layui Vitest / mocker 4.1.10 | 用户本次明确授权的最小安全补丁 4.1.11，无业务功能改动 |
| DOMPurify 3.4.15 两条 Low | 3.4.17，保留 Apache-2.0 许可选择 |

Jimp 中间候选 0.22.12 会引入 file-type 16.5.4 Moderate，因此未保留：最终 Jimp 1.6.1 的依赖声明已使用修复后的 file-type ^21.3.3。没有把 phin 或 file-type 跨主版硬塞到旧 API 消费者。补丁只适配调用边界，独立复核逐字符确认没有其他 minified 源码变化；来源、许可证、升级后移除条件见 `patches/README.md` 与 THIRD-PARTY-NOTICES。

参考：[phin 官方公告](https://github.com/advisories/GHSA-x565-32qp-m3vf)、[selector-parser 官方公告](https://github.com/advisories/GHSA-rj75-hqrm-r3gf)。修复版本和许可证取自 npm 官方 registry。

### 审计与构建失败的实际处理

生产门槛由 High 收紧至 Low，exceptions 仍为 `[]`。审计器按每个 finding/version 组核对五个级别的计数；未知严重级别、未知元数据键、元数据不一致、无路径或伪装为数组的 advisories 均拒绝，Critical 禁止例外的底线保留。新增回归先观察失败，再实施修复；只读复核指出的未知元数据漏报已关闭。

集中小程序构建先后发现旧 shared 缺少 `join`、DCloud 内嵌 vue-i18n 9.1.9 依赖已移除的 `handleFlatJson`。修复实际版本组合，并在应用 Vite post config hook 中以精确包名别名选择已修复的 9.14.5，覆盖 DCloud 后注入的旧运行时。启用 `__INTLIFY_JIT_COMPILATION__`，以消息 AST 解释执行，保持小程序禁止动态代码生成的要求。回归直接读取该配置、打包真实发行，并在禁止字符串代码生成的 VM 中验证中文插值、英文回退及语言切换；没有放开 eval。

Layui 冻结治理门禁只调整已授权的 manifest 安全补丁：其他 tracked 文件仍与 ec6eae92 逐文件零差异，manifest 以原 JSON 仅替换 Vitest 4.1.11 后整对象比较；其他依赖、脚本和新字段均不可变化，非忽略新文件仍禁止。没有增加目录通配放行，也没有恢复冻结客户端的持续交付。

### 冻结客户端既有退役债务

额外运行 Layui 完整历史单测得到 109 通过、17 失败（47 文件，3 个失败文件），失败集中在当前用户响应及 Grid 偏好等公共契约的旧夹具。使用本地原 Vitest 4.1.10 对相同三个文件对照，精确复现相同 17 项失败（另 6 项通过）。因此不归因于本次 4.1.11 安全补丁，也不将它们记为通过。按 rules/client-frontend.md §5.2/5.4，公共契约已批准演进后，冻结客户端的不兼容作为退役债务记录，本批不扩张或修补其功能。其构建结果另行列明。

### 本批集中验证

原始日志前缀为 `fullnet-client-all-`，位于同一系统临时目录。前批通过记录只在相关输入未变化时复用，不替代本批受影响消费者验证。

### 临时资源与命令策略记录

真实容器测试仅创建名称为 `fullnet-client-all-audit-<UUID>` 的隔离 Redis，完成后 stop/remove；未停止既有共享 SQL Server / MySQL / Redis。未生成根 artifacts 或复制整个工作区。

pnpm patch 的任务源文件提取目录 `C:/Users/Administrator/AppData/Local/Temp/fullnet-client-all-dcloud-patch` 已核实为精确 DCloud 发行，且无进程使用。以下原生 PowerShell 清理命令返回 `rejected: blocked by policy`（命令策略拒绝，未标识 Auto-review）；未更换解释器、嵌套 shell 或扩大路径重试，目录仍保留。永久 `patches/` 源码未受影响。

```powershell
$patchWork = (Resolve-Path -LiteralPath "$env:TEMP/fullnet-client-all-dcloud-patch").Path; $tempRoot = (Resolve-Path -LiteralPath $env:TEMP).Path; if ((Split-Path $patchWork -Parent) -ne $tempRoot -or (Split-Path $patchWork -Leaf) -ne 'fullnet-client-all-dcloud-patch') { throw 'Unexpected patch work path' }; if (Get-CimInstance Win32_Process -Filter "Name = 'node.exe'" | Where-Object { $_.CommandLine -like '*fullnet-client-all-dcloud-patch*' }) { throw 'Patch work directory is still active' }; Remove-Item -LiteralPath $patchWork -Recurse -Force; Test-Path -LiteralPath $patchWork
```

| 本批命令 / 检查 | 实际结果 |
| --- | --- |
| `pnpm install --frozen-lockfile` | 通过；所有 13 workspace，resolution skipped |
| `pnpm audit --json --registry=https://registry.npmjs.org` | exit 0，Info/Low/Moderate/High/Critical 全部 0，advisories={}，969 个依赖 |
| `pnpm audit:clients` / `pnpm licenses list --prod --json` | 均 exit 0；Low 门槛、空例外 |
| `pnpm test:workspace` | 35/35，0 跳过；审计策略 15、braces 15、工具链 4、工作区 1 |
| `pnpm --filter @fullnet/admin exec vitest run --maxWorkers=2 --testTimeout=15000` | 285 文件、1293/1293，0 跳过；640.23s |
| 共享客户端完整 Unit | client-contracts 290、admin-i18n 13、admin-form-designer 8，全部通过；uni-app 最终配置重跑 144/144 |
| uni-app 类型 / 三目标构建 | 类型检查、H5、微信、支付宝全部通过；最终 post hook 配置为实际输入 |
| `pnpm --filter @fullnet/admin build` | Vue 类型与生产构建通过 |
| `pnpm test:bundle-budgets` | 3/3，初始 69 chunk；1,494,792 bytes（+4.79%）、gzip 389,555（+3.35%）；预算未提高 |
| H5 Edge 浏览器 | 7/7、0 跳过、0 flaky；17.9s；新的本地开发服务器与 API 夹具，另检查本批新 H5 生产产物 |
| `pnpm test:governance` / `pnpm test:integration:tooling` | 85/85、153/153，全通过；冻结精确补丁门禁已纳入 |
| `pnpm --filter @fullnet/admin-real-stack-e2e test:provisioner` | 43/43，通过 |
| 真实 Testcontainers 消费链 | 创建并启动 Redis 8.6、映射端口后实际 PING/PONG、停止与删除全部通过；剩余任务容器查询为空 |
| 冻结 Layui 额外完整 Unit / build | Unit 109 通过、17 既有失败；build 失败：原 menus.js 消费已不存在的 HOST_MENU_ASSIGNABLE_PERMISSIONS。HEAD 同样存在该消费且共享 index 未导出，非本批 Vitest 或依赖升级引入；按上文冻结退役债务记录 |

Node 回归、单测、构建、浏览器均使用本批最终锁定依赖。构建通过标准 withTestRun 编译名额运行，失败后保留日志；已通过的最终三目标 / Vue 构建未重复。冻结 Layui 的既有构建失败不阻断其余独立检查，之后以独立 heavy 入口执行预算 / 浏览器，复用本批已生成的产物，不重建、不复用开发服务器。H5 5175 监听已退出。纯客户端范围不运行无关后端 / 双库 / Native，微信与支付宝仍只声明构建验收，未进行真机或开发者工具验收。

独立只读复核确认审计器边界、Jimp 唯一调用补丁、Dockerode CommonJS 消费、精确版本 / 锁文件、许可、Layui 精确冻结门禁及 i18n post hook 合并顺序；提出的元数据阻断已修复并复核关闭。i18n VM 用例不冒充小程序平台实测，平台兼容结论以实际三目标构建为限。

本批原始记录：`fullnet-client-all-audit-before.json`、`fullnet-client-all-audit-candidate.json`、`fullnet-client-all-policy-{red,review-red}.log`、`fullnet-client-all-workspace-delivery.log`、`fullnet-client-all-admin-unit.log`、`fullnet-client-all-shared-unit.log`、`fullnet-client-all-uni-unit-final.log`、`fullnet-client-all-layui-baseline.log`、`fullnet-client-all-build-browser{,-final,-complete,-verified}.log`、`fullnet-client-all-browser.log`、`fullnet-client-all-uniapp-e2e.json`、`fullnet-client-all-container.log`、`fullnet-client-all-provisioner.log`、`fullnet-client-all-governance-final.log`、`fullnet-client-all-tooling.log`、`fullnet-client-all-affected-plan.log`、`fullnet-client-all-frozen-final.log`、`fullnet-client-all-audit-complete.json`、`fullnet-client-all-audit-gate-complete.log`、`fullnet-client-all-licenses-complete.json`。

最终 `pnpm test:integration:affected:plan -- --snapshot client-audit-all-severities-20261020` 确认 16 个任务差异、仅 integration-tooling，已通过对应 153 项（最终规划输出为 `fullnet-client-all-affected-plan-final.log`）。`git diff --check` 返回 0；仅提示既有 LF/CRLF 转换，无空白错误。最终分支 main、HEAD `49345c3c9823360071433adb4928e09691a35d62` 未变，工作区 214 项包含既有多个任务差异；未覆盖无关修改，未提交、推送或部署。
