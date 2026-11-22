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

批量入口要求这四份文件为 JSON 对象，每份最多 1,048,576 个字符。common 与每个角色的 image 配置合并后，registry/repository/tag 必须一致，tag 须显式设置为有效镜像标签且不能为 latest；例如在 common.json 中统一提供 image.tag，并避免角色覆盖为另一候选。不同角色仍使用仓库名后的 -migrator/-worker/-api 后缀。缺失标签、混合版本、异常类型或无效 JSON 在 Chart 打包、备份、DDL 和升级之前拒绝；单角色入口不受此批量检查影响。同名标签不是镜像内容摘要证明，仍须按候选镜像完整性和实际运行证据核对内容，不据此绕过报表切版门禁。

只有已确认不涉及报表输出格式切版、无需专门旧消费者门禁的普通升级，才使用显式执行模式：

```bash
sudo /usr/local/lib/fullnet/Release-Application.sh "$CANDIDATE_CHART" all --ordinary-upgrade
```

执行顺序固定 Migrator → Worker → API。只在 Migrator 前启动备份，沿原流程启用/恢复临时 DDL；恢复并核对原值后才能继续 Worker。API 完成后沿原入口刷新网关。任一阶段失败立即停止，不执行后续角色，也不自动回滚已经完成的数据库或宿主发布。涉及报表格式变化仍须先走本文开头及 reporting-output-cutover.md 的既有专用门禁，不能用 ordinary 标志跳过。

成功回执保留 role/action/chartSha256，增加有序 roles、completedRoles 及四份 valuesSha256；plan 的 completedRoles 为空，不能当成发布完成。candidateImageTag 为本批冻结 values 实际核对的统一版本，单角色为 null，表示未执行跨角色版本认证；该字段不代表镜像内容不可变或运行版本已验证。批量 JSON 在任意对象层级出现重复键，或含 NaN/Infinity 时，在外部发布命令前拒绝，避免解析器对同一配置得出不同结果。失败标准错误报告 completedRoles 和 ddlRestorePending，标准输出无成功回执；只有完成实际 DDL 恢复和暂存清理才输出成功。执行与先前预览是不同调用，应逐项比较候选及四份配置摘要；文件变化时重新审查，不以旧预览批准新输入。单角色入口仍可独立使用。

定向入口 helm-runtime-focused / ubuntu-role-batch 复用一个隔离环境集中覆盖直接受影响的单角色控制流与批量顺序、冻结、失败停止、DDL 恢复、部分进度；真实 Helm 3/4 只做渲染，不连接发布集群。计划核对不证明镜像内容、真实数据库迁移、线上切换、恢复目标或容量。

回执另提供 chartContentSha256：对归档内普通文件按路径排序，记录文件长度和原始字节 SHA-256，再以固定 JSON 编码计算整体摘要；不纳入 tar/gzip 的打包时间、属主和权限。相同 Chart 重新打包时 chartSha256 可能改变，跨调用核对应比较 chartContentSha256、四份 valuesSha256 及 candidateImageTag；chartSha256 继续证明该次归档字节与调用内冻结完整性。内容身份不替代签名、审批、注册表镜像摘要或实际发布验证。链接、重复/不安全路径、空清单、超过 4096 个普通文件或单文件超过 16 MiB 的包会被拒绝；既有批准回执缺少该字段时须重新取得候选内容证据。

该手动入口可选 v3.16.4、v4.0.0 或现场读到的 v4.3.1；两个 Helm 4 版本仅执行相应真实渲染方法，不重复受控控制流集合。v4.3.1 在执行前独立校验官方归档 SHA-256、与安装二进制逐字节比较及版本号。[现场同版定向作业](https://github.com/yan041108/Full.NET/actions/runs/44902114817/job/136840004791) 已用 v4.3.1+gc0d08d0 验证当前三角色同包渲染与候选版本回执；这份隔离结果不表示目标节点已完成预演或升级。

批量入口支持追加 --expected-plan FILE，在所有渲染后、任何备份/DDL/升级前，自动核对 chartContentSha256、四份 valuesSha256 与 candidateImageTag；可同时用于 --plan 和 --ordinary-upgrade。该文件须为 root 所有、不可组/全局写、无链接的普通文件，最多 64 KiB，且是 fullnet-production 的 all/plan、正确顺序 roles、空 completedRoles 的 schemaVersion=1 回执。文件在本次读取候选前冻结并纳入后续摘要保护，不读取变化后的原回执。

保存每次独立的 plan 文件时由 root 创建，禁止覆盖已有回执，再显式用于候选复核或普通升级：

```bash
sudo install -d -m 0700 /var/lib/fullnet/release-plans
sudo bash -c 'set -C; umask 077; /usr/local/lib/fullnet/Release-Application.sh "$1" all --plan > "$2"' \
  fullnet-plan "$CANDIDATE_CHART" /var/lib/fullnet/release-plans/expected.json
sudo /usr/local/lib/fullnet/Release-Application.sh "$CANDIDATE_CHART" all --plan \
  --expected-plan /var/lib/fullnet/release-plans/expected.json
```

确认普通升级的既有门禁后，将第二条调用的 --plan 改为 --ordinary-upgrade 才执行升级。成功回执 expectedPlanMatched=true 表示匹配该次预期输入；未提供时为 null。相同 Chart 重新打包的归档摘要不同仍可匹配，内容、配置或版本漂移必须重新审查并产生新 plan。匹配不表示签名、授权、实际镜像认证或允许绕过报表切版门禁；单角色拒绝此参数，已执行回执和缺少内容身份的旧回执也拒绝。

## 验证

需要从固定 main 取得正式框架源码候选时，手动运行 source-candidate 工作流。它复用现有 build-source-bundle.mjs，前后核对干净提交，独立验证清单覆盖、无符号链接及全部受管文件 SHA-256，上传 fullnet-source-<完整提交> 产物，保留三天；内含源码归档和 candidate.json，后者绑定归档/清单摘要、源码提交和文件数。输出仅为 framework-source，不是已编译应用、独立应用安装包或运行镜像，也不表示完成迁移、现场预演或发布。运行时构建/部署/容量三个状态明确为 false。

[首个固定候选作业](https://github.com/yan041108/Full.NET/actions/runs/44903514174) 绑定 a5f1d34356dad20e3c114e2550aa5bda98689328，实际核对 6,581 个受管文件；从作业 Artifacts 下载后，按 candidate.json 核对源码归档 SHA-256，再解压到独立目录检查 framework-manifest.json。超过保留期重新运行得到新的显式源码提交，不能把之后 main 的提交号写成旧归档的来源。工作流仅用 GitHub 干净检出，不采纳本地未提交文件，正式分发白名单以构建器为准。

需要已编译的 Linux x64 JIT 候选时，手动运行 linux-jit-candidate 工作流。它在干净 main 上顺序发布 Migrator、Worker、Api，采用框架依赖模式（需要目标 .NET 10 运行时），上传 fullnet-linux-jit-<完整提交>，保留三天。产物包含三个角色的 tar.gz 和 candidate.json；先核对 sourceCommit、runtimeIdentifier、各 archiveSha256，再按 files 清单核对解包后的文件摘要。归档根目录直接包含 Host DLL、deps.json 与 runtimeconfig.json，可作为前述 Pack-OciImage.py 的应用输入。

[首个三角色 JIT 作业](https://github.com/yan041108/Full.NET/actions/runs/44903920154) 绑定 06caddffeb4f227f54acc38543a7d8f436c105a8，实际使用 SDK 10.0.404，分别验证 Migrator 291、Worker 296、Api 298 个文件及归档读回摘要。Development 配置已排除，链接、私钥及环境文件被拒绝。回执仅 jitPublishVerified=true；进程启动、Native AOT、OCI 镜像、数据库迁移、部署和容量仍未验证，下载包不能替代后续发布门禁。产物过期后重新运行，并使用新回执记录的源码提交。

需要 OCI 镜像候选时，手动运行 linux-oci-candidate，以 jit_run_id 指定同仓成功的 main/linux-jit-candidate 手动作业。入口核对正式来源、JIT 回执及所有输入摘要，复用 Pack-OciImage.py；首次从 MCR 获取运行时，后两角色离线复用该镜像的运行时层，三者必须完全一致。输出 fullnet-linux-oci-<应用完整提交>，保留三天，包含三个 .oci.tar、各自 .manifest.json 及 candidate.json；镜像引用为 docker.io/library/fullnet-<角色>:<应用完整提交>。下载后先按 roles.<角色>.archiveSha256 核对归档，按 manifestDigest 核对 OCI manifest，再使用既有导入和发布门禁；同名 sidecar 仅为 manifest，不代表已经导入或部署。

[首个三角色 OCI 作业](https://github.com/yan041108/Full.NET/actions/runs/44904939460) 输入 jit_run_id=44903920154，应用 sourceCommit=06caddffeb4f227f54acc38543a7d8f436c105a8，打包 packagingCommit=a879e7ed16b45291d922c4cc76d55fdd04dd2aad；两者分别表示程序集与打包入口来源。回执记录 MCR runtimeBaseManifestDigest、共享运行时层及各镜像摘要，实际核对全部 OCI blob、角色入口、UID、应用 DiffID 与文件字节。仅 imageBuildVerified=true，导入、启动、迁移、部署、AOT 和容量仍为 false。重新打包会重新取得上游基础镜像，必须使用新回执摘要，不能仅凭应用标签认定运行时未变化；不向注册表或发布节点上传。

需要对现有候选做真实导入验证时，手动运行 linux-oci-import，以 oci_run_id 指定成功的 main/linux-oci-candidate 作业。它核对归档、manifest 和各 blob，使用 Skopeo 导入隔离执行机的本机 Docker，实际比较入口、参数、用户、目录、环境和全部 RootFS DiffID；不重新构建镜像。随后只在禁网、只读且资源受限的容器中运行 dotnet --list-runtimes，检查 NETCore/ASP.NET Core 10。本轮专属镜像与容器会在独立清理步骤回收并检查零残留，不连接目标集群或启动业务 Host。

[首个实际候选导入作业](https://github.com/yan041108/Full.NET/actions/runs/44905410676) 输入 oci_run_id=44904939460，三角色均通过；Artifacts 中 fullnet-oci-import-44905410676 的 verification.json 绑定原候选回执摘要、应用/打包/验收提交、镜像 ID、层 DiffID 和实际运行时读数，保留三天。其 imageImportVerified/runtimeDiscoveryVerified=true，runtimeStartupVerified/databaseMigrationVerified/deploymentVerified/nativeAotVerified/capacityVerified=false。此回执补充原 candidate.json，不改写已上传候选的历史状态；运行时发现不能作为业务启动、迁移或现场发布验收。

需要进一步检查真实 Host 启动时，在同一工作流显式选择 verify_hosts=true。它创建不映射宿主端口的 Docker 内网与一次性 MySQL，使用随机本轮凭据，确认空库、实际备份并恢复到独立校验库后，仅向 Migrator 提供现有 UUID/Naming Contract 维护参数；API/Worker 不继承维护参数。随后运行原镜像入口，检查 live/startup/ready 的 Healthy 响应及正常停止。Host 保持 UID 1654、只读根、资源限额，归档目录使用独立临时挂载；凭据与容器数据随本轮清理，不修改镜像或部署配置。

[首个 Host 启动通过作业](https://github.com/yan041108/Full.NET/actions/runs/44906515239) 使用同一 OCI 候选，在 Testing/MySQL 下实际完成迁移（journal 397 条）、API/Worker 共六个健康端点及两个正常停止检查。verification.json 的 hostVerification 记录环境、Provider、数据库实际镜像摘要、空库备份 SHA-256/恢复校验和具体结果；顶层 runtimeStartupVerified/databaseMigrationVerified=true 仅适用于该测试组合，Production 配置、SQL Server、完整业务、Native AOT、部署和容量均未获认证。默认 verify_hosts=false 仍只执行导入与运行时发现，不能引用其他作业的启动结果。

生产模式的隔离启动检查使用 verify_hosts=true、host_environment=Production，默认环境仍为 Testing。夹具创建本轮 CA/MySQL 服务证书并校验 DNS SAN，数据库强制 TLS、Host 使用 VerifyFull；另外启动带密码的真实 Redis，加载本轮真实 RSA 签名密钥和 Data Protection PFX，挂载共享 Key Ring。云 Provider 占位凭据通过私有 env-file 注入，内网不允许外部调用。只读根、UID 1654 和资源限额保持，只有共享挂载增加本轮目录组访问；证书、私钥和随机凭据不上传。

[首个生产模式启动通过作业](https://github.com/yan041108/Full.NET/actions/runs/44907594656) 复用相同应用/镜像候选，实际完成 Production/MySQL 迁移、六个健康端点和两个正常停止检查；MySQL 观察到 36 次 TLS 接入。verification.json 的 hostVerification.productionConfigurationVerified=true 只表示该隔离配置启动通过，productionEvidence 记录 CA/PFX 摘要、Redis 实际镜像、VerifyFull/强制 TLS 和禁用临时签名等证据；不证明 Key Ring 跨实例解密、实际云 Provider、SQL Server、现场部署、完整业务、生产备份恢复或容量。回执保留三天，使用时核对应用、打包和验收三类提交，不能直接替代发布环境准入。

SQL Server 的当前候选启动检查选择 verify_hosts=true、host_provider=SqlServer、host_environment=Testing；默认 Provider 仍为 MySql，SqlServer/Production 组合会明确拒绝。夹具复用仓库既有 SQL Server 2022 CU14 测试镜像，在独占内网使用随机密码和临时数据库存储，不映射端口。实际 BACKUP CHECKSUM、VERIFYONLY 及恢复至独立空库后核对，再运行正式 Migrator 与两业务 Host；仅 Migrator 获得本轮 Contract 维护参数。Testing 连接启用加密并信任隔离自签名证书，不能据此宣称验证了服务器 TLS 身份。

[首个 SQL Server 候选作业](https://github.com/yan041108/Full.NET/actions/runs/44971433580) 对同一应用/OCI 候选完成空库恢复、397 条 journal、六个健康端点及两个正常停止检查，资源清理通过。Artifacts 的 verification.json 明确 environment=Testing、provider=SqlServer、productionConfigurationVerified=false、tlsServerIdentityVerified=false，记录数据库实际镜像摘要、备份摘要和三类源码身份。此结果补充 MySQL 的既有证据，不表示 SQL Server 生产配置、完整业务、现场恢复/部署、Native AOT 或容量已获认证；不触发其他 Provider 或全量矩阵。

共享 Key Ring 的独立进程检查选择 verify_keyring=true，可以同时保持 verify_hosts=false，避免重复启动数据库。入口从已导入 API/Worker 镜像各提取实际 Full.NET.Hosting.dll 并核对摘要一致，只编译最小 JIT 探针；运行时仅把探针自己的三个文件挂载到 /app，Hosting 程序集仍来自原镜像。探针使用原 AddFullNetDataProtection 注册和 Production 配置，独立非 root、只读、禁网容器双向加解密并重新加载落盘密钥；消费进程禁用自动生成密钥。分别检查错误 purpose、篡改内容、仅替换证书、仅替换 Key Ring 目录时的精确密码学拒绝，并检查实际密钥 XML 有 encryptedSecret、无明文 masterKey。不上传证书私钥、令牌或随机明文。

[Key Ring 专项通过作业](https://github.com/yan041108/Full.NET/actions/runs/44991798834) 对同一候选通过上述检查，实际一个落盘密钥受加密保护。verification.json 的 keyRingVerification 记录候选 Hosting SHA-256、双向/重载/四类拒绝和 encryptedKeyCount；scope=independent-processes-in-candidate-images、businessHostConsumerVerified=false、nativeAotVerified=false。它证明该候选注册与介质的独立进程边界，不代表业务 API/Worker 已执行某个消费链路、证书轮换或现场跨节点恢复。未选择 verify_hosts 的本轮 runtimeStartupVerified/databaseMigrationVerified 仍为 false，其他作业的 Host 结论单独保留。

证书轮换的独立进程检查使用 verify_keyring=true、keyring_mode=rotation，默认仍为 basic。专项在本轮介质中用新活动证书和旧历史证书读取旧密文，显式创建新密钥，并核对新密文公开头部的 KeyId 确实切换。仅新证书必须可读取新代、拒绝旧代；仅旧证书必须拒绝新代；以旧证书为活动证书并保留新证书历史解密材料时，两代均应可读。两个 key XML 均检查落盘加密，原密钥不删除或撤销。

[轮换专项通过作业](https://github.com/yan041108/Full.NET/actions/runs/44993805923) 的 verification.json 在 keyRingVerification.rotation 中记录新旧 KeyId/PFX 摘要、新密钥实际使用、历史读取、移除历史配置后的拒绝、保留材料的配置回退及 encryptedKeyCount=2。作用范围仍是候选镜像内的独立进程，businessHostConsumerVerified/productionRotationVerified=false；不表示现场轮换、证书退役或业务消费者恢复已认证。本轮不启动数据库/业务 Host，也不把 basic 未执行的拒绝项写成新鲜通过；现有发布环境应按自己的轮换顺序和历史密文生命周期评估，不能直接删除旧证书。

介质恢复的独立进程检查使用 verify_keyring=true、keyring_mode=recovery。夹具只为本轮无撤销记录的 Key Ring 创建加密 key XML ZIP、PFX 和精确摘要清单，不是生产通用备份器，遇到未知条目拒绝。PFX 密码不在备份中，恢复时从夹具独立 Secret 配置供给；实际恢复到新目录后，探针仅挂载恢复目录/证书及最小 Data Protection 配置，读取原密文并反向加解密。损坏归档和缺 PFX 要求在创建恢复目录前失败。

[恢复专项通过作业](https://github.com/yan041108/Full.NET/actions/runs/44994510715) 实际恢复一个加密密钥，确认原备份摘要不变。随后只在原介质创建新密钥，核对新载荷 KeyId，恢复快照拒绝该载荷且仍可读取旧载荷，证明没有回退读取原目录或沿用旧进程缓存。verification.json 的 keyRingVerification.recovery 记录 ZIP/清单/PFX 摘要和这些检查，businessHostConsumerVerified/productionRecoveryVerified=false；不证明业务消费者、撤销/多代证书介质、数据库/文件/会话联合恢复或真实跨节点恢复。本轮备份、PFX、恢复目录和秘密配置随暂存回收，下载产物仅为非秘密验收回执。

实际模块保护器检查选择 verify_keyring=true、keyring_mode=consumers。该 JIT 专项在角色镜像内按固定类型/方法清单调用原 Identity、Notifications、Payments 程序集，不复制实现或扩展 internal 类型可见性，依赖从原 Host deps 解析。覆盖通知端点、LDAP、OAuth、TOTP、支付 API 密钥和商户私钥六类用途的双向跨进程保护/解保护，以及不同用途、支付两种用途相互使用和通用探针密文的拒绝。仅调用保护器组件，不连接 LDAP、OIDC、通知或支付渠道。

[实际保护器专项通过作业](https://github.com/yan041108/Full.NET/actions/runs/44995063566) 完成六类用途、八个精确密码学拒绝，并用候选 NotificationRecipientEndpointProtector 完成独立介质恢复、损坏/缺证书写前拒绝、原值读取及备份后新密钥快照拒绝。verification.json 的 keyRingVerification.consumers 记录实际模块 DLL 摘要和各项结果，moduleProtectorComponentsVerified=true；businessHostPipelineVerified/externalDeliveryVerified/paymentChannelVerified/nativeAotVerified=false。它证明真实模块组件与介质边界，不能替代 API 授权、数据库记录、Worker 队列、验证码消费、真实收件或商户认证。

下载可信来源的 verification.json 后，可只读核对多份回执是否属于同一候选：

```bash
python3 eng/deploy/ubuntu/Verify-CandidateEvidence.py \
  --source-commit 06caddffeb4f227f54acc38543a7d8f436c105a8 --oci-run-id 44904939460 \
  --receipt /secure/receipts/mysql-production.json \
  --receipt /secure/receipts/sqlserver-testing.json \
  --receipt /secure/receipts/module-protectors.json \
  --require-host Production/MySql --require-host Testing/SqlServer --require-modules
```

当前支持上述 Host 范围和 consumers 模块回执；不要求重跑完整 Provider 矩阵。校验应用/打包提交、OCI 作业、原候选回执摘要、各角色镜像/归档/manifest/层身份，以及声明的健康/停止/用途隔离/通知恢复结果。验收代码提交可以不同，候选绑定必须相同；遗漏所需范围、混用候选、数值冒用布尔值或越界部署声明均拒绝。输入普通 JSON 文件每份最多 256 KiB、最多 16 份，拒绝重复键和非有限数，不修改输入或连接环境；成功 stdout 为 status=consistent，失败 exit=1、stderr 为稳定机器码，不回显输入内容。

三份上述实际 Actions 回执已按该命令核对一致；只修改一份 API manifest 摘要会得到 receipt_image_binding_mismatch。consistent 仅说明所选回执内部一致，receiptAuthenticityVerified/deploymentAuthorized/deploymentVerified/businessHostPipelineVerified/externalChannelsVerified/capacityVerified 仍为 false；来源真伪由操作者另行核对，不能把此工具当作签名、授权或现场通过。下载回执不代替正式发布门禁，额外未知范围须另行审查。

- `tests/deployment/foundation-preset-release.test.mjs`
- 发布清单：`docs/verification/f16-release-checklist.md`
