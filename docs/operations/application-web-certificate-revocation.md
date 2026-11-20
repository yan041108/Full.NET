# 内网 Web 证书吊销检查

此流程用于 Full.NET Ubuntu 内网网关。Web 证书沿用应用 CA、现有私钥和证书请求。签发模板增加 `http://<网关 IPv4>/pki/application.crl`。网关在 HTTP 上直接返回签名的 DER CRL，使客户端可以在 HTTPS 握手前获取吊销信息。MySQL 使用单独的 CA 和 CRL。

## 启用

1. 确认网关 IPv4 与现有 Web 证书模板中的 IP SAN 一致，客户端可访问网关 TCP 80。备份 Web 证书和续签模板。维护窗口内避免与既有 TLS 续签任务并发。
2. 通过既有网关部署流程更新 `Configure-InternalEdge.py` 渲染的 NGINX 配置。确认 `nginx -t` 通过并重载网关。配置提供 `/pki/application.crl` 的精确 HTTP 路由，响应类型为 `application/pkix-crl`。
3. 将 `Manage-ApplicationCrl.py` 安装到 `/usr/local/lib/fullnet/`，将 `fullnet-application-crl.service` 与 `fullnet-application-crl.timer` 安装到 `/etc/systemd/system/`。以 root 运行以下命令，其中 `GATEWAY_IP` 是发布网关的 IPv4。

   ```bash
   python3 /usr/local/lib/fullnet/Manage-ApplicationCrl.py --configure "$GATEWAY_IP"
   systemctl daemon-reload
   systemctl enable --now fullnet-application-crl.timer
   systemctl start fullnet-application-crl.service
   systemctl list-timers fullnet-application-crl.timer
   ```

4. 从独立客户端读取 HTTP CRL，核对颁发者、签名和 `nextUpdate`。在 Windows 上使用 Schannel curl、应用 CA 和正式入口执行严格 HTTPS 探测。续签后检查正在提供的证书序列号并重复该探测。

## 刷新与恢复

- 计时器每日生成有效期七天的 CRL。签名验证通过后才原子替换公开文件。刷新失败时用 `journalctl -u fullnet-application-crl.service` 定位，并在现有 CRL 到期前恢复。
- `/etc/fullnet/application-pki/` 包含应用 CA 的吊销账本和 CRL 序号，纳入受控 PKI 备份。恢复时使用与 CA 匹配的当前账本。已有公开 CRL 而账本缺失时，配置命令会要求恢复原账本。
- Web 续签继续使用 `web.extensions`。配置命令重复执行会验证并重载现有合格证书。换证重载失败或响应丢失时，脚本恢复换证前的证书和模板，再重新校验并重载原证书。恢复成功仍以失败退出，不能当成新证书部署成功。发布后仍应独立确认网关实际提供的证书。
- 客户端可按 CRL 有效期缓存副本，吊销后的现场复核应包含独立客户端和实际取得的 CRL 时间。

## 换证失败的状态与恢复材料

OpenSSL、NGINX 校验及每次重载命令均有十五秒期限，命令标准输出/错误不直接进入回执。失败退出码为 1，标准错误是一条 JSON，包含固定 code、filesRestored、rollbackReloadAcknowledged 和 recoveryDirectory。文件已恢复与恢复重载已获命令确认是两个独立状态；命令确认不代替线上握手观察。幂等重试的现有合格证书重载失败时不重新签发或改写文件。

如果文件恢复失败，code=web_rollback_file_failed、filesRestored=false，recoveryDirectory 指向证书目录下本轮 0700 的 .web-crl-* 目录。该目录不自动删除：未恢复文件的 web.pem.previous 或 web.extensions.previous 是需要保留的恢复副本。先停止其他维护调用，核对回执目录、现有文件和相应 previous 副本，按受控恢复流程处理并重新校验/重载；不要盲目删除目录或仅依赖文件存在判定恢复成功。若原文件已恢复但重载仍失败，code=web_rollback_reload_failed、filesRestored=true、rollbackReloadAcknowledged=false，须单独核对网关进程和实际 TLS 证书。脚本不因 Web 换证失败回退已经发布的签名 CRL 或丢弃吊销账本。

定向入口 helm-runtime-focused / web-certificate-recovery 使用临时 CA/CRL、原私钥、独立 NGINX master 与真实回环 TLS：先确认新证书已经提供给客户端，再模拟控制响应丢失并核对恢复后的旧证书指纹及 master PID；另验正常配置和幂等重试。只下载/解包测试二进制，不安装系统服务、不访问发布节点；不替代目标节点、Windows Schannel、客户端 CRL 缓存或强杀/掉电恢复验证。NGINX 重载与旧 worker 排空语义见[官方控制说明](https://nginx.org/en/docs/control.html)。

## 2026-11-18 发布环境复验

网关 `192.168.31.229` 已安装现有 CRL 管理脚本和每日刷新定时器，使用既有应用 CA 和 Web 私钥签发带分发点的 Web 证书。HTTP CRL 返回 200 和 `application/pkix-crl`，签名验证通过。Windows Schannel curl 使用系统信任和默认吊销校验访问 live、ready 均为 200。

首次配置前的证书、模板和网关配置保存在目标机 `/etc/fullnet/certificates/.crl-enable-backup-0ia7d1oh/`。重复执行配置后证书序列号相同，NGINX 检查及重载通过，严格客户端 ready 继续为 200。刷新服务退出码为 0，定时器为 active。CA 吊销账本按上述受控 PKI 备份要求维护。
