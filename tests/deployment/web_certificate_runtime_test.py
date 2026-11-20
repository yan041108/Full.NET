"""真实 NGINX/TLS 验证证书切换与恢复，全部 CA、私钥、CRL 和进程归本轮临时环境。"""
import hashlib
import ipaddress
from pathlib import Path
import shutil
import signal
import socket
import ssl
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch

from test_application_crl import APP, create_pki


class WebCertificateRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.nginx = shutil.which("nginx")
        self.assertIsNotNone(self.nginx, "真实 NGINX 是必需条件，缺失不能跳过。")
        self.temporary = tempfile.TemporaryDirectory(prefix="fullnet-web-tls-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.application, self.public = self.root / "application", self.root / "public"
        self.enterContext(patch.multiple(APP, ROOT=self.application, PUBLIC=self.public))
        self.certificates, self.ca, self.key = create_pki(self.application)
        self.web, self.extensions = self.certificates / "web.pem", self.certificates / "web.extensions"
        self.old_certificate = self.web.read_bytes()
        self.old_extensions = self.extensions.read_bytes()
        self.old_key = (self.certificates / "web.key").read_bytes()
        self.old_ca = self.ca.read_bytes()
        self.old_fingerprint = self.fingerprint(self.old_certificate)
        self.address = ipaddress.IPv4Address("127.0.0.1")
        with socket.socket() as reserve:
            reserve.bind(("127.0.0.1", 0))
            self.port = reserve.getsockname()[1]
        self.configuration = self.root / "nginx.conf"
        self.configuration.write_text(
            f"pid {self.root / 'nginx.pid'};\nerror_log {self.root / 'error.log'};\n"
            "worker_processes 1; worker_shutdown_timeout 1s; events { worker_connections 32; }\n"
            "http { access_log off;\n"
            + "".join(f"{name}_temp_path {self.root / name};\n"
                      for name in ("client_body", "proxy", "fastcgi", "uwsgi", "scgi"))
            + f"server {{ listen 127.0.0.1:{self.port} ssl;\n"
            + f"ssl_certificate {self.web}; ssl_certificate_key {self.certificates / 'web.key'};\n"
            + "location / { return 200 'verified'; } } }\n")
        self.base = [self.nginx, "-p", str(self.root) + "/", "-c", str(self.configuration)]
        self.process = subprocess.Popen([*self.base, "-g", "daemon off;"],
                                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.addCleanup(self.stop_nginx)
        self.context = ssl.create_default_context(cafile=str(self.ca))
        self.await_certificate(self.old_fingerprint)
        self.master = (self.root / "nginx.pid").read_text()
        self.reloads = 0
        self.lose_response = False
        self.applied_fingerprint = None

    @staticmethod
    def fingerprint(pem):
        return hashlib.sha256(ssl.PEM_cert_to_DER_cert(pem.decode())).hexdigest()

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

    def await_certificate(self, expected):
        deadline = time.monotonic() + 5
        observed = None
        while time.monotonic() < deadline:
            try:
                with socket.create_connection(("127.0.0.1", self.port), timeout=0.5) as raw:
                    with self.context.wrap_socket(raw, server_hostname="127.0.0.1") as tls:
                        observed = hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest()
                        tls.sendall(b"GET / HTTP/1.0\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n")
                        response = tls.recv(4096)
                        if observed == expected and response.startswith(b"HTTP/1.1 200"):
                            return
            except OSError:
                pass
            time.sleep(0.03)
        log = (self.root / "error.log").read_text()[-1500:] if (self.root / "error.log").exists() else ""
        self.fail(f"本轮 TLS 证书未收敛：{observed}; {log}")

    def reload(self):
        subprocess.run([*self.base, "-t"], check=True, capture_output=True, timeout=15)
        subprocess.run([*self.base, "-s", "reload"], check=True, capture_output=True, timeout=15)
        self.reloads += 1
        expected = self.fingerprint(self.web.read_bytes())
        self.await_certificate(expected)
        if self.lose_response and self.reloads == 1:
            # 真实 TLS 握手先证明已切换，随后才模拟控制响应丢失。
            self.assertNotEqual(expected, self.old_fingerprint)
            self.applied_fingerprint = expected
            raise subprocess.TimeoutExpired("systemctl", 15)

    def assert_unchanged_materials_and_master(self):
        self.assertEqual((self.certificates / "web.key").read_bytes(), self.old_key)
        self.assertEqual(self.ca.read_bytes(), self.old_ca)
        self.assertIsNone(self.process.poll())
        self.assertEqual((self.root / "nginx.pid").read_text(), self.master)
        self.assertEqual(list(self.certificates.glob(".web-crl-*")), [])
        APP.openssl("crl", "-inform", "DER", "-in", self.public / "application.crl", "-verify", "-CAfile", self.ca, "-noout")

    def test_applied_certificate_timeout_restores_real_tls_certificate(self):
        self.lose_response = True
        with patch.object(APP, "reload_nginx", side_effect=self.reload):
            with self.assertRaises(APP.WebCertificateFailure) as failure:
                APP.manage(self.address)
        self.assertEqual(str(failure.exception), "web_reload_failed")
        self.assertTrue(failure.exception.files_restored)
        self.assertTrue(failure.exception.rollback_reload_acknowledged)
        self.assertIsNotNone(self.applied_fingerprint)
        self.assertEqual(self.reloads, 2)
        self.assertEqual(self.web.read_bytes(), self.old_certificate)
        self.assertEqual(self.extensions.read_bytes(), self.old_extensions)
        self.await_certificate(self.old_fingerprint)
        self.assert_unchanged_materials_and_master()

    def test_success_and_retry_keep_same_live_certificate_and_original_key(self):
        with patch.object(APP, "reload_nginx", side_effect=self.reload):
            APP.manage(self.address)
            current = self.web.read_bytes()
            metadata = self.web.stat()
            current_fingerprint = self.fingerprint(current)
            self.assertNotEqual(current_fingerprint, self.old_fingerprint)
            self.await_certificate(current_fingerprint)
            APP.manage(self.address)
        self.assertEqual(self.web.read_bytes(), current)
        self.assertEqual((self.web.stat().st_ino, self.web.stat().st_mtime_ns),
                         (metadata.st_ino, metadata.st_mtime_ns))
        self.assertIn("crlDistributionPoints", self.extensions.read_text())
        self.assertEqual(self.reloads, 2)
        self.assert_unchanged_materials_and_master()


if __name__ == "__main__":
    unittest.main()
