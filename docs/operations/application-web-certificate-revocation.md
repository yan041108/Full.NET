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
- Web 续签继续使用 `web.extensions`。配置命令重复执行会验证并重载现有合格证书。换证重载失败时脚本恢复换证前的证书和模板。发布后仍应独立确认网关实际提供的证书。
- 客户端可按 CRL 有效期缓存副本，吊销后的现场复核应包含独立客户端和实际取得的 CRL 时间。
