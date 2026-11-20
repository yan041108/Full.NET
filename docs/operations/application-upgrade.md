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

## 对象存储公开证书同步

在已安装对象存储的 secondary 节点执行既有入口，参数为公开 PKI 路由所在的 primary IPv4：

```bash
sudo /usr/local/lib/fullnet/Receive-ObjectCertificate.sh PRIMARY_IPV4
```

默认从 primary 的 80 端口 /pki/object-server.pem 下载到 /etc/fullnet/object-storage/ 下本轮私有暂存目录；使用固定地址、二十秒下载预算和 32 KiB 上限，不经过环境代理、不跟随重定向，要求 HTTP 200。证书是公开数据，内容身份依赖本地受信 application-ca.pem、server.env 中的 OBJECT_BIND_ADDRESS、sslserver 用途/有效期和原 object.key 的公钥，不能把网络可达或 HTTP 成功当作证书可信。仅接收签发器当前提供的单个 PEM 叶证书，不轮换本地 CA 或私钥，不判断 CRL/OCSP 或发行版本新旧。

接收器对输入文件做所有权、类型、链接和权限检查；私钥仅在内存中经 stdin 提取公钥，不复制到新的文件、命令参数或输出。独占 .certificate-sync.lock，提交前再次核对本地输入未变；不使用或覆盖固定 object.next。校验完成后在同文件系统原子替换为当前执行者所有、fullnet-storage 组、0640 的 object.pem，同证书且元数据正确时不改 inode。自定义部署可显式给出 --directory、--port、--group；目录必须由调用者拥有且不可被组/其他用户写入，正式默认目录仍要求 root。

成功输出一条 status=updated/unchanged JSON，含公开证书 SHA-256 指纹及本轮 certificateInstalled；成功回执的 serviceActivationVerified=false。提交前失败保留原证书，错误只报告固定码；如提交已完成但暂存清理失败，则失败回执明确 certificateInstalled=true，不能推断旧文件仍有效。可处理的 TERM/INT 会清理本轮暂存，强杀、掉电和在线对象服务是否加载新证书仍须另行核对。

提供 fullnet-object-certificate.service/.timer 源码，保留 UTC 18:10 每日同步及最多五分钟随机延迟；服务期限九十秒、内存 128 MiB，仅允许写证书目录。这些单元不会由本次开发或测试安装、启用或触发。定向入口 helm-runtime-focused / object-certificate 只使用临时 CA/私钥、真实 OpenSSL 与回环 HTTP/TLS，检查签名/IP/用途/有效期/公钥、并发锁、原子读者、旧文件保留及提交状态，不访问部署节点。验证语义依据 [OpenSSL 3.0 证书验证选项](https://docs.openssl.org/3.0/man1/openssl-verification-options/)与 [公钥导出](https://docs.openssl.org/3.0/man1/openssl-pkey/)。

## 双节点集群网络核对

使用已有 Kubernetes 授权，显式指定两个不同的 Ready、可调度节点和目标 context：

```bash
eng/deploy/ubuntu/Verify-Cluster.sh PRIMARY_NODE SECONDARY_NODE \
  --kubectl kubectl --context TARGET_CONTEXT
```

省略 --kubectl 时复用 k3s kubectl；不修改 kubeconfig，也不创建或提升权限。调用方需要读取节点，以及创建/读取/等待/删除本轮 Namespace、Pod 和执行 Pod 命令的权限。入口会实际创建随机 fullnet-infra-check-* 命名空间，仅在其中创建两个探针 Pod，不操作业务命名空间。--image 可显式给出预载镜像或固定摘要；默认 rancher/mirrored-library-busybox:1.37.0，镜像须提供 sh/httpd/nslookup/wget/sleep。

先核对节点 UID、Ready 与可调度性，再把 server/client 固定在不同节点。Pod 非 root、根文件系统只读、禁用自动挂载 SA Token，资源上限 CPU 100m/内存 64 MiB、生命周期 300 秒，服务内容只写本轮 emptyDir。实际执行集群 DNS 解析和客户端到服务端 Pod IP 的 HTTP 请求，响应必须是本轮随机值；复核 Pod UID、容器/镜像及节点 UID 未更换后再清理。每次命令有超时和 4 MiB 输出上限，不把远端原始错误或 kubeconfig 回显到终端。

创建响应丢失或信号中断也按本轮标签核对归属；清理时命名空间 UID/标签不符会停止删除并报告失败。必须等待删除并再次查询确认不存在，才输出 status=passed、namespaceDeleted=true 的单条 JSON；删除失败/假删除只输出失败和本轮命名空间定位码，不能按旧摘要判定成功。节点更换、命名空间终止异常和物理主机掉电仍需人工复核，不以本探针证明业务故障接管、外部渠道或容量。

手动聚焦入口 helm-runtime-focused / cluster-network 运行十七条受控命令用例及一次真实 Kind 两工作节点场景。Kind v0.30.0 使用官方校验和，Kubernetes v1.34.0 节点镜像固定发布摘要，独立 kubeconfig 和唯一集群不连接发布环境；结束复核临时命名空间和集群容器均已删除。它验证同一执行机上两个 Kubernetes 节点的实际协议，生产 CNI、物理故障域和 Capacity-not-verified 状态另保留。固定测试镜像依据 [Kind v0.30.0 发布记录](https://github.com/kubernetes-sigs/kind/releases/tag/v0.30.0)。

## Ubuntu 发布运行证据封存

### 生成角色更新 OCI 包

使用既有 Linux x64 JIT publish tar 和受信基础 OCI 包，不依赖 Docker 守护进程：

```bash
python3 eng/deploy/ubuntu/Pack-OciImage.py --publish api-linux-x64.tar --role api \
  --tag CANDIDATE --base-oci reviewed-base.oci.tar --base-digest sha256:REVIEWED_MANIFEST_DIGEST \
  --cache /var/cache/fullnet/oci --output /srv/fullnet/releases/CANDIDATE/api.oci.tar
```

role 支持 api/worker/migrator；对应宿主 DLL、deps.json、runtimeconfig.json 必须存在且非空。基础 OCI 必须为单一 linux/amd64 镜像，引用的 SHA-256、大小和 rootfs 层数必须一致；--base-digest 固定预期镜像 manifest，不是 OCI 外层 tar 摘要。需要先从受信交付取得该值，摘要校验不代替来源认证。不传 --base-oci 时仍仅从微软 MCR 的 ASP.NET 10.0 下载，解析 amd64 平台并验证描述符；建议显式固定 --base-digest，成功 JSON 会记录实际基础 manifest，下载使用 curl 的证书校验和有界期限。

离线输入可为干净运行时包，或旧 Full.NET 包；后者仅当最后应用历史标记、app/ 路径和未压缩 DiffID 均核对通过时才替换旧应用层，运行时层字节和摘要保持不变；Docker 基础层的 mediaType 会规范为 OCI tar+gzip，避免混合类型 manifest 被导入器拒绝。拒绝归档越界、重复规范路径、链接、设备、FIFO、whiteout 和文件/目录冲突。只接受 JIT 入口，不把 Native AOT 产物包当作 DLL 发布包。应用层排序、权限、时间戳和 gzip 头规范化，同样的受信基础与应用内容可复现；点文件保持原名，可执行位保留，容器使用 UID 1654。

在线 blob 下载为重定向预留至少 64 KiB 的有界传输预算，但最终大小与 SHA-256 仍须精确匹配 descriptor；只跟随 HTTPS，最多五次。下载、IO、元数据、归档失败分别输出固定分类（下载/IO 含数字退出码），不输出原始 URL、路径或远端响应。

生成 OCI tar 与同名 .manifest.json，目标存在即拒绝覆盖；归档最后排他提交，普通失败清理本轮 sidecar 和暂存文件，成功输出 manifest/base/archive 三种摘要。输出父目录须预先存在；cache 是操作者明确指定的复用位置，不作为发布成功标记。每个归档条目与展开范围上限 4 GiB、最多五万成员，基础最多 128 层，仅支持 sha256 和 tar/gzip 层。强杀或掉电可能留下独立 sidecar，保留并核对后另选输出，不自动删除历史文件或把 sidecar 当作完整镜像。

定向验收 helm-runtime-focused / oci-packaging 使用合成基础层和 publish 内容，仅证明 OCI 结构、摘要和打包行为，不证明运行时来源、真实程序集启动、容器导入或 Kubernetes 发布。参考 [OCI 描述符](https://github.com/opencontainers/image-spec/blob/main/descriptor.md)与 [DiffID 规则](https://github.com/opencontainers/image-spec/blob/main/config.md)。

需要验证真实导入与入口时，选择手动入口 helm-runtime-focused / oci-runtime：只发布 tests/deployment/fixtures/oci-runtime/ 的无业务依赖 .NET 10 探针，正式 Pack-OciImage.py 从 MCR 获取运行时并输出基础/候选/归档摘要，通过 Skopeo 导入本机 Docker。三个角色的入口分别以镜像自身 UID 1654 启动，运行时再核对工作目录、环境、点文件和应用代次；API 离线更新验证运行时层保留、旧应用文件消失，错误代次须真实退出失败。

该入口仅接受显式隔离 Linux 和本地 Unix socket Docker context，且要求 DOCKER_HOST 与该 context 一致。导入器使用执行机已有 sudo -n 权限处理归档内的 root 所有权，目标守护进程显式绑定，容器应用权限不提升。探针容器禁用网络、根文件系统只读、CPU 1/内存 128 MiB/进程 64，命令有硬期限；构建输出、下载缓存和 OCI 包均在系统临时目录，结束只删除本轮唯一标签/名称的镜像和容器，不执行 prune 或复制整仓。Skopeo 使用 [OCI archive 到 Docker daemon 传输](https://github.com/containers/image/blob/main/docs/containers-transports.5.md)，不向远端仓库推送。通过结果只认证镜像传输与 .NET 探针入口，不替代 Full.NET 三个完整业务宿主、数据库迁移、Native AOT 或生产 Kubernetes 的验收。

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

## Ubuntu 三角色集中发布

普通升级可一次准备三个角色，减少重复打包和配置读取。先用同一候选 Chart 集中预览：

```bash
sudo /usr/local/lib/fullnet/Release-Application.sh "$CANDIDATE_CHART" all --plan
```

一次调用持有同一个发布锁，只打包一次 Chart，在第一次渲染前冻结 common.json、migrator.json、worker.json、api.json。三个角色分别强制 production=true 和单一角色；全部渲染通过后才允许任何备份、DDL 或 Helm upgrade。每次渲染/升级前及集中渲染结束均复核冻结输入摘要；原配置后续变化不改变本批，冻结副本篡改则立即拒绝。

只有已确认不涉及报表输出格式切版、无需专门旧消费者门禁的普通升级，才使用显式执行模式：

```bash
sudo /usr/local/lib/fullnet/Release-Application.sh "$CANDIDATE_CHART" all --ordinary-upgrade
```

执行顺序固定 Migrator → Worker → API。只在 Migrator 前启动备份，沿原流程启用/恢复临时 DDL；恢复并核对原值后才能继续 Worker。API 完成后沿原入口刷新网关。任一阶段失败立即停止，不执行后续角色，也不自动回滚已经完成的数据库或宿主发布。涉及报表格式变化仍须先走本文开头及 reporting-output-cutover.md 的既有专用门禁，不能用 ordinary 标志跳过。

成功回执保留 role/action/chartSha256，增加有序 roles、completedRoles 及四份 valuesSha256；plan 的 completedRoles 为空，不能当成发布完成。失败标准错误报告 completedRoles 和 ddlRestorePending，标准输出无成功回执；只有完成实际 DDL 恢复和暂存清理才输出成功。执行与先前预览是不同调用，应逐项比较候选及四份配置摘要；文件变化时重新审查，不以旧预览批准新输入。单角色入口仍可独立使用。

定向入口 helm-runtime-focused / ubuntu-role-batch 复用一个隔离环境集中覆盖直接受影响的单角色控制流与批量顺序、冻结、失败停止、DDL 恢复、部分进度；真实 Helm 3/4 只做渲染，不连接发布集群。计划核对不证明镜像内容、真实数据库迁移、线上切换、恢复目标或容量。

## 验证

- `tests/deployment/foundation-preset-release.test.mjs`
- 发布清单：`docs/verification/f16-release-checklist.md`
