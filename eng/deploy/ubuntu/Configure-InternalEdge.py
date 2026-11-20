#!/usr/bin/env python3
"""配置单入口 HTTPS 网关、全局额度与代理信任边界；只更新入口，不处理业务数据。"""
import argparse
import ipaddress
import json
import os
import pathlib
import stat
import subprocess
import sys

RELEASES = pathlib.Path("/srv/fullnet/releases")
CURRENT = pathlib.Path("/srv/fullnet/current-web")
PENDING = pathlib.Path("/srv/fullnet/.next-web")
CONFIGURATION = pathlib.Path("/etc/nginx/conf.d/fullnet.conf")
WEB_CERTIFICATE = pathlib.Path("/etc/fullnet/certificates/web.pem")
WEB_KEY = pathlib.Path("/etc/fullnet/certificates/web.key")


class AdmissionFailure(Exception):
    """固定准入码，失败不回显文件内容或底层命令响应。"""


def require(condition, code):
    if not condition:
        raise AdmissionFailure(code)


def trusted(path, directory=False, private=False):
    info = path.lstat()
    require((stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)) and
            info.st_uid == 0 and info.st_mode & 0o022 == 0, "deployment_path_permissions_invalid")
    require(path.resolve(strict=True) == path, "deployment_path_symlink_invalid")
    if not directory:
        require(info.st_size > 0 and info.st_nlink == 1, "deployment_file_invalid")
    if private:
        require(info.st_mode & 0o007 == 0, "web_key_permissions_invalid")


def address(value):
    parsed = ipaddress.IPv4Address(value)
    require(not (parsed.is_loopback or parsed.is_link_local or parsed.is_multicast or parsed.is_unspecified
                 or int(parsed) == 0xFFFFFFFF), "gateway_address_invalid")
    return str(parsed)


def check_pointer():
    if CURRENT.exists() or CURRENT.is_symlink():
        require(CURRENT.is_symlink() and CURRENT.lstat().st_uid == 0, "frontend_pointer_invalid")
        require(CURRENT.resolve(strict=False).is_relative_to(RELEASES), "frontend_pointer_outside_releases")
    # lexists 语义同时拒绝悬空链接，不能覆盖其他调用遗留的暂存。
    require(not PENDING.exists() and not PENDING.is_symlink(), "frontend_pending_conflict")


def preflight(primary, secondary, management, release):
    # 所有准入都在集群、包管理器、服务与文件修改之前执行，不能使用可被 -O 删除的 assert。
    require(os.geteuid() == 0, "root_required")
    primary, secondary, management = map(address, (primary, secondary, management))
    require(primary != secondary, "infrastructure_nodes_not_distinct")
    trusted(RELEASES, directory=True)
    trusted(CURRENT.parent, directory=True)
    release_path = pathlib.Path(release).resolve(strict=True)
    require(release_path != RELEASES and release_path.is_relative_to(RELEASES), "release_outside_releases")
    cursor = release_path
    while cursor != RELEASES:
        trusted(cursor, directory=True)
        cursor = cursor.parent
    frontend = release_path / "frontend"
    trusted(frontend, directory=True)
    trusted(frontend / "index.html")
    check_pointer()
    parent = CONFIGURATION.parent
    while not parent.exists() and not parent.is_symlink():
        parent = parent.parent
    trusted(parent, directory=True)
    if CONFIGURATION.exists() or CONFIGURATION.is_symlink():
        trusted(CONFIGURATION)
    for path in (WEB_CERTIFICATE, WEB_KEY):
        trusted(path.parent, directory=True)
        trusted(path, private=path == WEB_KEY)
    return primary, secondary, management, release_path


def run(command, **kwargs):
    return subprocess.run(command, check=True, capture_output=True, text=True, **kwargs).stdout


def apply(resource):
    run(["k3s", "kubectl", "apply", "--server-side", "--field-manager=fullnet-deployment", "-f", "-"], input=json.dumps(resource))


def render_configuration(primary):
    return f'''# HTTPS 只面向受控内网；未识别的 Host 不进入应用。
# 日志不读取原始请求行、参数、认证头或 Referer。
log_format fullnet_edge escape=json
  '{{"time":"$time_iso8601","id":"$request_id","method":"$request_method","path":"$uri",'
  '"status":$status,"duration":"$request_time","upstreamStatus":"$upstream_status",'
  '"upstreamDuration":"$upstream_response_time"}}';
map $http_upgrade $fullnet_connection {{ default upgrade; '' close; }}
# 只在静态 Vue 入口收紧脚本；OIDC 与 Scalar 保留自己的页面策略。
map $uri $fullnet_csp {{
  default "frame-src 'self'; object-src 'none'; base-uri 'self'";
  /index.html "script-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'";
}}
limit_req_zone $server_name zone=fullnet_global:10m rate=100r/s;
limit_conn_zone $server_name zone=fullnet_connections:10m;
server {{ listen {primary}:80 default_server; server_name _;
  access_log /var/log/nginx/access.log fullnet_edge;
  # NGINX 请求错误日志格式固定，可能带完整请求行。请求诊断使用上面的结构化日志。
  error_log /dev/null;
  location = /pki/application.crl {{
    alias /var/lib/fullnet-pki/application.crl;
    types {{ }}
    default_type application/pkix-crl;
    add_header Cache-Control "no-cache";
  }}
  location /pki/ {{ alias /var/lib/fullnet-pki/; }}
  location / {{ return 308 https://{primary}$request_uri; }}
}}
server {{
  listen {primary}:443 ssl default_server;
  server_name {primary} ubuntu-24-01;
  ssl_certificate /etc/fullnet/certificates/web.pem;
  ssl_certificate_key /etc/fullnet/certificates/web.key;
  ssl_protocols TLSv1.2 TLSv1.3;
  ssl_session_cache shared:FullNetTLS:10m;
  server_tokens off;
  access_log /var/log/nginx/access.log fullnet_edge;
  # NGINX 请求错误日志格式固定，可能带完整请求行。请求诊断使用上面的结构化日志。
  error_log /dev/null;
  if ($host !~ ^({primary.replace('.', '[.]')}|ubuntu-24-01)$) {{ return 444; }}
  root /srv/fullnet/current-web;
  index index.html;
  client_max_body_size 32m;
  client_body_timeout 30s;
  client_header_timeout 15s;
  keepalive_timeout 30s;
  limit_conn fullnet_connections 200;
  limit_conn_status 429;
  limit_req_status 429;
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy strict-origin-when-cross-origin always;
  add_header Strict-Transport-Security "max-age=86400" always;
  add_header Content-Security-Policy $fullnet_csp always;
  location / {{ try_files $uri $uri/ /index.html; }}
  location = /index.html {{ expires -1; }}
  location ~* \\.(?:js|css|woff2?|png|svg|bcmap)$ {{ try_files $uri =404; expires 7d; }}
  location ~ ^/(api|hubs|connect|identity|\\.well-known|openapi|scalar|health)(/|$) {{
    limit_req zone=fullnet_global burst=200 nodelay;
    proxy_pass http://fullnet_api_upstream;
    proxy_bind {primary};
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto https;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $fullnet_connection;
    proxy_connect_timeout 5s;
    proxy_read_timeout 60s;
    proxy_send_timeout 60s;
  }}
}}
'''


def configure(primary, secondary, management, release_path):
    # Traefik 保留集群内入口能力，释放主机标准 80/443 给唯一的全局额度网关。
    apply({"apiVersion": "helm.cattle.io/v1", "kind": "HelmChartConfig", "metadata": {"name": "traefik", "namespace": "kube-system"},
           "spec": {"valuesContent": "service:\n  spec:\n    type: ClusterIP\n"}})
    apply({"apiVersion": "v1", "kind": "Service", "metadata": {"name": "fullnet-api-headless", "namespace": "fullnet-production"},
           "spec": {"clusterIP": "None", "selector": {"app.kubernetes.io/instance": "fullnet-api", "app.kubernetes.io/component": "api"},
                    "ports": [{"name": "http", "port": 8080, "targetPort": 8080}]}})
    apply({"apiVersion": "networking.k8s.io/v1", "kind": "NetworkPolicy", "metadata": {"name": "fullnet-state-services", "namespace": "fullnet-production"},
           "spec": {"podSelector": {}, "policyTypes": ["Egress"],
                    "egress": [{"to": [{"ipBlock": {"cidr": primary + "/32"}}], "ports": [{"protocol": "TCP", "port": 3306}, {"protocol": "TCP", "port": 6379}, {"protocol": "TCP", "port": 80}]},
                               {"to": [{"ipBlock": {"cidr": secondary + "/32"}}], "ports": [{"protocol": "TCP", "port": 6379}, {"protocol": "TCP", "port": 8334}]}]}})
    apply({"apiVersion": "networking.k8s.io/v1", "kind": "NetworkPolicy", "metadata": {"name": "fullnet-internal-edge", "namespace": "fullnet-production"},
           "spec": {"podSelector": {"matchLabels": {"app.kubernetes.io/component": "api"}}, "policyTypes": ["Ingress"],
                    "ingress": [{"from": [{"ipBlock": {"cidr": primary + "/32"}}], "ports": [{"protocol": "TCP", "port": 8080}]}]}})
    run(["python3", "/usr/local/lib/fullnet/Configure-GatewayTrust.py", primary])
    environment = dict(os.environ, DEBIAN_FRONTEND="noninteractive")
    run(["apt-get", "install", "-y", "--no-install-recommends", "nginx"], env=environment)
    run(["systemctl", "stop", "nginx"])
    default = pathlib.Path("/etc/nginx/sites-enabled/default")
    if default.is_symlink():
        default.unlink()
    check_pointer()
    PENDING.symlink_to(release_path / "frontend")
    PENDING.replace(CURRENT)
    configuration = render_configuration(primary)
    CONFIGURATION.write_text(configuration)
    run(["ufw", "allow", "from", str(ipaddress.IPv4Network(management + "/24", strict=False)), "to", primary, "port", "443", "proto", "tcp"])
    run(["ufw", "allow", "from", str(ipaddress.IPv4Network(management + "/24", strict=False)), "to", primary, "port", "80", "proto", "tcp"])
    print("内网 HTTPS 配置已写入；启动前须刷新就绪 API 上游并通过 nginx -t。")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--render", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("parameters", nargs="+")
    options = parser.parse_args(argv)
    if len(options.parameters) != (1 if options.render else 4):
        parser.error("--render 需要一个 IPv4；配置或 --check 需要 primary secondary management release。")
    started = False
    try:
        if options.render:
            print(render_configuration(address(options.parameters[0])), end="")
        else:
            inputs = preflight(*options.parameters)
            if options.check:
                print(json.dumps({"status": "admitted", "changesApplied": False,
                                  "runtimeActivationVerified": False}, separators=(",", ":")))
            else:
                started = True
                configure(*inputs)
        return 0
    except AdmissionFailure as error:
        code = str(error)
    except (OSError, ValueError, subprocess.SubprocessError):
        code = "gateway_configuration_failed"
    print(json.dumps({"status": "failed", "code": code, "modificationsMayHaveOccurred": started},
                     separators=(",", ":")), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
