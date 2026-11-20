# 应用升级

Full.NET 生成应用升级遵循 **Migrator → Worker → Api** 顺序，与 `eng/deploy/Invoke-FullNetRelease.ps1` 一致。

该顺序适用于完成升级前置检查的常规发布，不是数据库回退后的联合恢复流程。发布脚本会直接启动 Worker，不能代替[联合恢复门禁](./application-recovery.md#联合恢复顺序)中的外发隔离、权限复核及对账。

## 升级前

1. 备份数据库与 `framework-manifest.json` 摘要
2. 在预发环境执行 `diagnose --profile production`
3. 核对新版本迁移编号闭包与模块预设
4. 停止应用框架目录的写入与运行进程，保留本地定制；升级预览报告冲突时先人工合并，不强行覆盖

### 报表导出在途任务

包含报表导出输出身份变更的升级先按[报表切版手册](./reporting-output-cutover.md)向所有旧 API 实例应用 `api.reportingExportAdmissionEnabled=false`，验证有权会话的导出提交返回 503，并等待滚动更新和在途创建请求结束。保持旧版 Worker 排空存量，再将旧 API 和 Worker 停稳。正式发布使用 `Invoke-FullNetRelease.ps1 -ReportingOutputCutover`，核对旧宿主持续停稳、主库未完成任务、残留租约、待确认上传和未绑定就绪文件；任一阻断项未清零就不切版。新 API 保持暂停，完成原文件读取及授权复核后才按手册放行。健康检查、租约到期或队列暂时为空不能替代这些门禁。

旧版上传成功但完成写入失败的处理中任务可能已有就绪文件，新版无法从旧文件名可靠推断该次输出行数；在当前授权仍有效且选中的就绪文件缺少可识别身份时，会保留处理中而不自动绑定或再次上传。遇到此类任务，应隔离相关执行、保留任务和文件证据并人工升级处置；在未确认文件归属、行数、授权及外部影响前，不得猜测行数、改写文件身份、删除文件、直接修改任务状态或混跑旧版消费者。当前没有已验证的通用修复工具，未解决的存量必须作为升级阻断项。回退到旧版前同样需要停止准入并排空或隔离新版在途操作，不能假定混版恢复安全。

## 升级步骤

涉及报表输出格式变更时，先完成[报表切版门禁](./reporting-output-cutover.md)，由发布负责人维持旧写入方停稳。

0. **预览受管框架升级**。从受信任的 Full.NET 源码树运行 `node scripts/templates/upgrade-framework.mjs --app <AppRoot> --package <NewTemplatePackage> --dry-run`。升级到固定候选时，预览和应用均传入 `--distribution-sha256 <SHA-256>`，摘要从受信任的发布记录获取。核对预览的 `updates`、`removals` 和 `conflicts` 后，以 `--apply` 替换 `--dry-run` 提交。退役文件仅在字节与旧清单一致时从新框架移除，原件保存在返回的 `recoveryRoot/previous` 中。已定制或缺失的退役文件需先解决冲突。应用自有 `src/` 由应用团队维护。
1. **Migrator**：应用 SQL Server/MySQL 成对迁移
2. **Worker**：滚动重启，等待 Outbox/Jobs 消费追平
3. **Api**：滚动重启，验证健康检查与关键读取路径

## Ubuntu/MySQL 的单角色发布步骤

`eng/deploy/ubuntu/Release-Application.sh CHART ROLE [--plan]` 接续 Ubuntu 参考部署，读取已审核的 `/etc/fullnet/release/common.json` 和对应角色 values。安装脚本到 `/usr/local/lib/fullnet/` 时使用 root 属主与 `0700`；migrator 依赖已经安装的备份服务，api 仍沿已有入口刷新网关。

`--plan` 先冻结两份 values、打包候选 Chart 并实际渲染，输出角色和候选摘要，不启动备份、不改 MySQL、不访问集群或升级资源。正式执行在该次调用内复用同一冻结包和 values，强制 production 及单一选定角色，防止配置漂移把 worker 发布到 api Release。重新执行会重新冻结输入，输入发生变化时需重新审查。每次命令只负责一个角色，发布协调者仍按 Migrator → Worker → API 推进。

```bash
sudo /usr/local/lib/fullnet/Release-Application.sh "$CANDIDATE_CHART" migrator --plan
```

migrator 在候选渲染通过、备份服务成功后才开启临时 DDL 设置；读取原值、确认启用、发布后恢复并复核，恢复失败不输出成功结果。SET 响应丢失也会执行恢复。包和渲染有六十秒期限，Helm 操作使用自身十分钟等待及外层硬期限。可处理的退出和 TERM/INT 会执行清理；强杀进程或主机异常后的参数状态仍需人工核对，不能由预览或脚本存在推断恢复已经完成。

成功清理本轮暂存目录；失败仅保留私有且有界的原始诊断，values 与候选副本会被移除，秘密不直接回显到终端。该普通角色步骤不承担报表输出切版、旧消费者排空或联合恢复；涉及这些变更时使用上方对应手册和正式门禁。

## Ubuntu 发布运行证据封存

### 生成角色更新 OCI 包

使用既有 Linux x64 JIT publish tar 和受信基础 OCI 包，不依赖 Docker 守护进程：

```bash
python3 eng/deploy/ubuntu/Pack-OciImage.py --publish api-linux-x64.tar --role api \
  --tag CANDIDATE --base-oci reviewed-base.oci.tar --base-digest sha256:REVIEWED_MANIFEST_DIGEST \
  --cache /var/cache/fullnet/oci --output /srv/fullnet/releases/CANDIDATE/api.oci.tar
```

role 支持 api/worker/migrator；对应宿主 DLL、deps.json、runtimeconfig.json 必须存在且非空。基础 OCI 必须为单一 linux/amd64 镜像，引用的 SHA-256、大小和 rootfs 层数必须一致；--base-digest 固定预期镜像 manifest，不是 OCI 外层 tar 摘要。需要先从受信交付取得该值，摘要校验不代替来源认证。不传 --base-oci 时仍仅从微软 MCR 的 ASP.NET 10.0 下载，解析 amd64 平台并验证描述符；建议显式固定 --base-digest，成功 JSON 会记录实际基础 manifest，下载使用 curl 的证书校验和有界期限。

离线输入可为干净运行时包，或旧 Full.NET 包；后者仅当最后应用历史标记、app/ 路径和未压缩 DiffID 均核对通过时才替换旧应用层，运行时层保持不变。拒绝归档越界、重复规范路径、链接、设备、FIFO、whiteout 和文件/目录冲突。只接受 JIT 入口，不把 Native AOT 产物包当作 DLL 发布包。应用层排序、权限、时间戳和 gzip 头规范化，同样的受信基础与应用内容可复现；点文件保持原名，可执行位保留，容器使用 UID 1654。

生成 OCI tar 与同名 .manifest.json，目标存在即拒绝覆盖；归档最后排他提交，普通失败清理本轮 sidecar 和暂存文件，成功输出 manifest/base/archive 三种摘要。输出父目录须预先存在；cache 是操作者明确指定的复用位置，不作为发布成功标记。每个归档条目与展开范围上限 4 GiB、最多五万成员，基础最多 128 层，仅支持 sha256 和 tar/gzip 层。强杀或掉电可能留下独立 sidecar，保留并核对后另选输出，不自动删除历史文件或把 sidecar 当作完整镜像。

定向验收 helm-runtime-focused / oci-packaging 使用合成基础层和 publish 内容，仅证明 OCI 结构、摘要和打包行为，不证明运行时来源、真实程序集启动、容器导入或 Kubernetes 发布。参考 [OCI 描述符](https://github.com/opencontainers/image-spec/blob/main/descriptor.md)与 [DiffID 规则](https://github.com/opencontainers/image-spec/blob/main/config.md)。

### 采集并封存运行快照

角色升级结束后，使用已有只读 Kubernetes 权限采集一次运行快照。入口不读取管理员密码或 Key Ring，不执行 MySQL，不写 Nginx 或修改工作负载；凭据交付、配置生效和密钥恢复各自按原步骤验证。

```bash
sudo python3 /usr/local/lib/fullnet/Finalize-Application.py /srv/fullnet/releases/RELEASE_TAG \
  --namespace fullnet-production --api-deployment fullnet-api --worker-deployment fullnet-worker \
  --api-image registry.example/fullnet-api:CANDIDATE --worker-image registry.example/fullnet-worker:CANDIDATE
```

两个预期镜像必须显式给出，推荐使用固定摘要引用。默认每角色 2 副本、健康端口 8080；可用 --replicas 1～4、--health-port、--kubectl 指定实际拓扑与工具。每角色副本须位于不同节点，同角色实际 imageID 须一致。默认 k3s，也支持独立 kubectl 可执行路径；凭据由已有 KUBECONFIG 提供，不放入命令参数。采集与 Release-Application.sh 共用 /run/fullnet-release.lock，锁冲突直接拒绝。

仅当 Deployment 已观察本代、目标/更新/就绪/可用数一致，选中 Pod 都为 Ready 且镜像匹配预期时，才逐 Pod 建立仅监听回环的健康隧道；HTTP 200 且正文 Healthy 才通过，Degraded、重定向和过大响应均拒绝。采集前后再次核对部署代次、Pod UID、容器身份和源摘要；中途滚动、重启或修改候选需重新采集，原始集群错误不回显。单个集群命令有二十秒期限和 4 MiB 输出上限，隧道启动十秒、健康请求三秒，退出回收本轮子进程。

前端默认 RELEASE_TAG/frontend；混合角色发布可用 --frontend 显式指定 releases 下另一前端目录，回执记录相对位置。候选至少包含 source-manifest.json、chart.tar、前端 index.html；只对前端文件和根目录明确的 manifest/OCI/chart 产物计算摘要，不复制源码或归档。拒绝链接和硬链接产物；前端最多一万文件，发布根产物最多 128 个。操作员应冻结候选的写入，脚本不代替发布包签名或产物来源认证。

全部通过后，在候选下原子创建私有 runtime-evidence/，包含 runtime.json、inputs.json 和精确 SHA256SUMS；旧目录一律拒绝覆盖。运行失败退出 1，stdout 不留成功回执，清理本轮暂存目录；成功只报告 sealed 及核对数量。这里的原子可见性不代替备份介质持久性，不写全局 COMPLETE/恢复标记，不证明数据库、密钥、外部渠道、RPO/RTO 或容量；保持 Capacity-not-verified。定向入口 helm-runtime-focused / release-evidence 只使用受控 kubectl、回环 HTTP 与临时文件，不访问真实集群。

## Expand/Contract

- 仅追加列/表时使用 Expand；删除或重命名列前必须 Contract 旧读路径
- 跨版本框架源码升级时合并 `framework/fullnet/`，保留 `src/` 业务所有权
- 升级器校验完整包摘要、清单副本、路径与链接，并重新投影应用既定模块预设和双库迁移清单；预览后的输入变化会拒绝提交。模块闭包改变须另行设计迁移。退役受管文件按预览清单处理，数据库结构变更仍由 Migrator 执行。
- 固定预设执行历史 203 UUID 修复时，Migrator 按清单内的建表来源保留所选表的完整校验与恢复；未选表必须同时不存在于数据库，否则拒绝预设漂移。变换仅匹配两库历史全文摘要，不改写资源或 DbUp 记账名称；无清单的完整底座仍执行原脚本。此适配不支持通过修改清单删除已有模块，也不替代模块新增/退役的数据迁移设计。
- 应用根目录的依赖锁文件、Vue 骨架和宿主属于应用资产，升级器只更新 `framework/fullnet/`；应用资产的兼容合并仍需开发者处理和验证。

## 回退

- 数据库：使用备份恢复或执行已验证的 down 脚本（若提供）
- 应用：回滚到上一版本镜像/源码包，保持迁移版本一致
- 框架源码：升级输出 `recoveryRoot`，其中 `previous/` 与 `previous-manifest.json` 保存原框架及根清单，成功后也不自动清理。普通提交失败会恢复原目录与清单；失败的新内容保留在恢复区。
- 进程中断：应用根目录存在 `.fullnet-upgrade.lock` 时，后续升级失败关闭。停止所有相关进程，按锁内 `recoveryRoot` 核对 `previous/`、`next/`、活动目录及两份清单；保留全部内容后恢复匹配的框架/清单。完成检查后再人工移除锁；不能直接重试或删除恢复区。源码恢复不代替数据库、密钥或对象文件恢复。

## 验证

- `tests/deployment/foundation-preset-release.test.mjs`
- 发布清单：`docs/verification/f16-release-checklist.md`
