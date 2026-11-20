"""真实临时 NGINX 与回环上游验证；适配器不调用宿主 systemctl 或修改 /etc/nginx。"""
import http.client
import http.server
import ipaddress
import json
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from test_edge_ready_upstreams import EDGE, endpoint, slices


class EdgeReloadRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.nginx = shutil.which("nginx")
        self.assertIsNotNone(self.nginx, "真实 NGINX 是必需条件，缺失不能跳过。")
        self.directory = tempfile.TemporaryDirectory(prefix="fullnet-edge-runtime-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.servers, self.threads = [], []
        self.addCleanup(self.stop_backends)
        port = 0
        for address, content in (("127.0.0.1", b"old"), ("127.0.0.2", b"new")):
            class Handler(http.server.BaseHTTPRequestHandler):
                body = content

                def do_GET(self):
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(self.body)))
                    self.end_headers()
                    self.wfile.write(self.body)

                def log_message(self, *_):
                    pass
            server = http.server.HTTPServer((address, port), Handler)
            port = server.server_port
            thread = threading.Thread(target=server.serve_forever)
            self.servers.append(server)
            self.threads.append(thread)
            thread.start()
        original_render = EDGE.render_upstream
        self.render = lambda addresses: original_render(addresses).replace(":8080 ", f":{port} ")
        self.target = self.root / "upstream.conf"
        self.previous = self.render(["127.0.0.1"]).encode()
        self.target.write_bytes(self.previous)
        with socket.socket() as reserve:
            reserve.bind(("127.0.0.1", 0))
            self.port = reserve.getsockname()[1]
        self.configuration = self.root / "nginx.conf"
        self.configuration.write_text(
            f"pid {self.root / 'nginx.pid'};\nerror_log {self.root / 'error.log'};\n"
            "worker_processes 1;\nworker_shutdown_timeout 1s;\nevents { worker_connections 32; }\n"
            f"http {{ access_log off; include {self.target};\n"
            + "".join(f"{name}_temp_path {self.root / name};\n"
                      for name in ("client_body", "proxy", "fastcgi", "uwsgi", "scgi"))
            + f"server {{ listen 127.0.0.1:{self.port}; location / {{\n"
            "proxy_connect_timeout 1s; proxy_read_timeout 1s; proxy_pass http://fullnet_api_upstream;\n"
            "} } }\n")
        self.base = [self.nginx, "-p", str(self.root) + "/", "-c", str(self.configuration)]
        self.process = subprocess.Popen([*self.base, "-g", "daemon off;"],
                                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.addCleanup(self.stop_nginx)
        self.await_response(200, b"old")
        self.master = (self.root / "nginx.pid").read_text()
        self.real_run = subprocess.run
        self.reloads = 0
        self.lose_first_response = False
        self.recovered = False

    def stop_backends(self):
        for server in self.servers:
            server.shutdown()
            server.server_close()
        for thread in self.threads:
            thread.join(3)
            self.assertFalse(thread.is_alive())

    def stop_nginx(self):
        if self.process.poll() is None:
            self.process.send_signal(signal.SIGQUIT)
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=3)
                self.fail("本轮 NGINX 未正常退出。")
        self.assertEqual(self.process.returncode, 0)

    def await_response(self, status, content=None):
        deadline = time.monotonic() + 5
        observed = None
        while time.monotonic() < deadline:
            connection = http.client.HTTPConnection("127.0.0.1", self.port, timeout=0.5)
            try:
                connection.request("GET", "/", headers={"Connection": "close"})
                response = connection.getresponse()
                body = response.read(4096)
                observed = (response.status, body)
                if response.status == status and (content is None or body == content):
                    return
            except OSError:
                pass
            finally:
                connection.close()
            time.sleep(0.03)
        self.fail(f"本轮代理响应未收敛：{observed!r}")

    def command(self, arguments, **kwargs):
        if arguments == ["nginx", "-t"]:
            return self.real_run([*self.base, "-t"], **kwargs)
        self.assertEqual(arguments, ["systemctl", "reload", "nginx"])
        self.reloads += 1
        result = self.real_run([*self.base, "-s", "reload"], **kwargs)
        if self.lose_first_response and self.reloads == 1:
            # 先证明真实 worker 已采用新配置，再模拟控制调用丢失响应。
            self.await_response(502)
            raise subprocess.TimeoutExpired("systemctl", 15)
        if self.lose_first_response:
            self.await_response(200, b"old")
            self.recovered = True
        return result

    def patches(self, snapshots):
        return (patch.object(EDGE, "POD_NETWORK", ipaddress.IPv4Network("127.0.0.0/8")),
                patch.object(EDGE, "render_upstream", self.render),
                patch.object(EDGE.subprocess, "check_output", side_effect=snapshots),
                patch.object(EDGE.subprocess, "run", side_effect=self.command))

    def assert_master_unchanged(self):
        self.assertIsNone(self.process.poll())
        self.assertEqual((self.root / "nginx.pid").read_text(), self.master)

    def test_applied_reload_timeout_restores_real_proxy_without_restart(self):
        self.lose_first_response = True
        network, render, lookup, commands = self.patches([json.dumps(slices())])
        with network, render, lookup, commands:
            with self.assertRaises(EDGE.RefreshFailure) as failure:
                EDGE.refresh(self.target)
        self.assertEqual(str(failure.exception), "edge_reload_failed")
        self.assertTrue(failure.exception.config_restored)
        self.assertTrue(failure.exception.rollback_reload_acknowledged)
        self.assertTrue(self.recovered)
        self.assertEqual(self.reloads, 2)
        self.assertEqual(self.target.read_bytes(), self.previous)
        self.assert_master_unchanged()
        self.await_response(200, b"old")

    def test_empty_routes_then_ready_recovery_changes_real_proxy(self):
        network, render, lookup, commands = self.patches([
            json.dumps(slices()), json.dumps(slices(endpoint("127.0.0.2", ready=True)))])
        with network, render, lookup, commands:
            self.assertEqual(EDGE.refresh(self.target), 0)
            self.await_response(502)
            self.assertEqual(EDGE.refresh(self.target), 1)
            self.await_response(200, b"new")
        self.assertEqual(self.reloads, 2)
        self.assert_master_unchanged()


if __name__ == "__main__":
    unittest.main()
