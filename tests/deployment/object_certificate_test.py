"""真实 OpenSSL、回环 HTTP/TLS 与文件系统验收；不读取或替换部署节点证书。"""
import contextlib
import fcntl
import http.server
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import socket
import ssl
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Receive-ObjectCertificate.py"
spec = importlib.util.spec_from_file_location("fullnet_object_certificate", SCRIPT)
receiver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(receiver)


def openssl(*arguments):
    result = subprocess.run(["openssl", *map(str, arguments)], capture_output=True, timeout=15)
    if result.returncode:
        raise RuntimeError("fixture_openssl_failed: " + result.stderr.decode()[-1500:])


class ObjectCertificateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.materials = tempfile.TemporaryDirectory(prefix="fullnet-object-cert-materials-")
        cls.addClassCleanup(cls.materials.cleanup)
        root = Path(cls.materials.name)
        cls.certificates = {}
        for name in ("ca", "other-ca"):
            openssl("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2", "-subj", "/CN=" + name,
                    "-addext", "basicConstraints=critical,CA:TRUE", "-addext", "keyUsage=critical,keyCertSign,cRLSign",
                    "-keyout", root / (name + ".key"), "-out", root / (name + ".pem"))
        for name in ("server", "wrong"):
            openssl("genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", root / (name + ".key"))
            openssl("req", "-new", "-key", root / (name + ".key"), "-subj", "/CN=object.invalid", "-out", root / (name + ".csr"))
        serial = 100
        for name, authority, key_name, address, purpose in (
            ("old", "ca", "server", "127.0.0.1", "serverAuth"),
            ("new", "ca", "server", "127.0.0.1", "serverAuth"),
            ("wrong-ca", "other-ca", "server", "127.0.0.1", "serverAuth"),
            ("wrong-ip", "ca", "server", "127.0.0.2", "serverAuth"),
            ("wrong-key", "ca", "wrong", "127.0.0.1", "serverAuth"),
            ("wrong-purpose", "ca", "server", "127.0.0.1", "clientAuth")):
            serial += 1
            extensions = root / (name + ".ext")
            extensions.write_text("basicConstraints=CA:FALSE\nsubjectAltName=IP:" + address +
                                  "\nextendedKeyUsage=" + purpose + "\n")
            certificate = root / (name + ".pem")
            openssl("x509", "-req", "-in", root / (key_name + ".csr"), "-CA", root / (authority + ".pem"),
                    "-CAkey", root / (authority + ".key"), "-set_serial", str(serial), "-days", "1",
                    "-extfile", extensions, "-out", certificate)
            cls.certificates[name] = certificate.read_bytes()
        (root / "index.txt").touch()
        (root / "serial").write_text("1000\n")
        (root / "issued").mkdir()
        (root / "ca.cnf").write_text("[ca]\ndefault_ca=issuer\n[issuer]\n"
            + "database=" + str(root / "index.txt") + "\nserial=" + str(root / "serial")
            + "\nnew_certs_dir=" + str(root / "issued") + "\ncertificate=" + str(root / "ca.pem")
            + "\nprivate_key=" + str(root / "ca.key") + "\ndefault_md=sha256\npolicy=subject\n"
            + "[subject]\ncommonName=supplied\n")
        openssl("ca", "-batch", "-notext", "-config", root / "ca.cnf", "-in", root / "server.csr",
                "-startdate", "20000101000000Z", "-enddate", "20010101000000Z",
                "-extfile", root / "new.ext", "-out", root / "expired.pem")
        cls.certificates["expired"] = (root / "expired.pem").read_bytes()
        cls.key = (root / "server.key").read_bytes()
        cls.ca = (root / "ca.pem").read_bytes()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="fullnet-object-cert-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.directory = self.root / "installation"
        self.directory.mkdir(mode=0o700)
        for name, value in {"server.env": b"OBJECT_BIND_ADDRESS=127.0.0.1\n", "object.key": self.key,
                            "application-ca.pem": self.ca, "object.pem": self.certificates["old"]}.items():
            (self.directory / name).write_bytes(value)
            (self.directory / name).chmod(0o640)
        self.payload = self.certificates["new"]
        self.status = 200
        self.requests = []
        self.before_response = None
        owner = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                owner.requests.append(self.path)
                if owner.before_response:
                    owner.before_response()
                self.send_response(owner.status)
                self.send_header("Location", "/unexpected-redirect")
                self.send_header("Content-Length", str(len(owner.payload)))
                self.end_headers()
                try:
                    self.wfile.write(owner.payload)
                except (BrokenPipeError, ConnectionResetError):
                    pass

            def log_message(self, *_):
                pass
        self.http = http.server.HTTPServer(("127.0.0.1", 0), Handler)
        self.thread = threading.Thread(target=self.http.serve_forever)
        self.thread.start()
        self.addCleanup(self.close_server)

    def close_server(self):
        self.http.shutdown()
        self.http.server_close()
        self.thread.join(3)
        self.assertFalse(self.thread.is_alive())

    def arguments(self):
        return ["127.0.0.1", "--directory", str(self.directory), "--port", str(self.http.server_port),
                "--group", str(os.getgid())]

    def cli(self):
        return subprocess.run([sys.executable, "-O", str(SCRIPT), *self.arguments()],
                              capture_output=True, text=True, timeout=10)

    def no_temporary_files(self):
        self.assertEqual([path for path in self.directory.glob(".certificate-*") if path.is_dir()], [])

    def failed(self, code):
        result = self.cli()
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertEqual(json.loads(result.stderr), {"status": "failed", "code": code, "certificateInstalled": False})
        self.assertEqual((self.directory / "object.pem").read_bytes(), self.certificates["old"])
        self.no_temporary_files()
        return result

    def test_valid_update_is_atomic_and_preserves_private_key_and_ca(self):
        certificate = self.directory / "object.pem"
        with certificate.open("rb") as previous:
            old_inode = certificate.stat().st_ino
            result = self.cli()
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(previous.read(), self.certificates["old"])
        self.assertEqual(certificate.read_bytes(), self.certificates["new"])
        self.assertNotEqual(certificate.stat().st_ino, old_inode)
        self.assertEqual(certificate.stat().st_mode & 0o777, 0o640)
        self.assertEqual(certificate.stat().st_gid, os.getgid())
        self.assertEqual((self.directory / "object.key").read_bytes(), self.key)
        self.assertEqual((self.directory / "application-ca.pem").read_bytes(), self.ca)
        report = json.loads(result.stdout)
        self.assertEqual(report["status"], "updated")
        self.assertTrue(report["certificateInstalled"])
        self.assertFalse(report["serviceActivationVerified"])
        self.assertEqual(self.requests, ["/pki/object-server.pem"])
        self.no_temporary_files()

    def test_same_valid_certificate_does_not_rewrite_inode(self):
        self.payload = self.certificates["old"]
        before = (self.directory / "object.pem").stat()
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["status"], "unchanged")
        after = (self.directory / "object.pem").stat()
        self.assertEqual((before.st_ino, before.st_mtime_ns), (after.st_ino, after.st_mtime_ns))

    def test_first_certificate_install(self):
        (self.directory / "object.pem").unlink()
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.directory / "object.pem").read_bytes(), self.certificates["new"])

    def test_wrong_ca_preserves_current_certificate(self):
        self.payload = self.certificates["wrong-ca"]
        self.failed("certificate_trust_or_identity_invalid")

    def test_wrong_ip_preserves_current_certificate(self):
        self.payload = self.certificates["wrong-ip"]
        self.failed("certificate_trust_or_identity_invalid")

    def test_wrong_private_key_preserves_current_certificate(self):
        self.payload = self.certificates["wrong-key"]
        self.failed("certificate_private_key_mismatch")

    def test_expired_certificate_is_rejected(self):
        self.payload = self.certificates["expired"]
        self.failed("certificate_trust_or_identity_invalid")

    def test_client_only_purpose_is_rejected(self):
        self.payload = self.certificates["wrong-purpose"]
        self.failed("certificate_trust_or_identity_invalid")

    def test_malformed_pem_is_not_echoed(self):
        self.payload = b"private-response-sentinel"
        result = self.failed("certificate_pem_invalid")
        self.assertNotIn("private-response-sentinel", result.stderr)

    def test_multiple_certificates_are_rejected(self):
        self.payload += self.certificates["old"]
        self.failed("certificate_pem_invalid")

    def test_oversized_download_is_rejected(self):
        self.payload = b"x" * 32769
        self.failed("certificate_download_failed")

    def test_redirect_is_not_followed(self):
        self.status = 302
        self.failed("certificate_http_status_invalid")
        self.assertEqual(self.requests, ["/pki/object-server.pem"])

    def test_http_failure_preserves_current_certificate(self):
        self.status = 503
        self.failed("certificate_download_failed")

    def test_concurrent_sync_is_rejected_before_download(self):
        path = self.directory / ".certificate-sync.lock"
        with path.open("w") as lock:
            path.chmod(0o600)
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.failed("certificate_sync_busy")
        self.assertEqual(self.requests, [])

    def test_private_key_permissions_are_checked_before_download(self):
        (self.directory / "object.key").chmod(0o644)
        self.failed("private_key_permissions_invalid")
        self.assertEqual(self.requests, [])

    def test_certificate_symlink_does_not_touch_target(self):
        certificate = self.directory / "object.pem"
        certificate.unlink()
        target = self.root / "untouched.pem"
        target.write_bytes(self.certificates["old"])
        certificate.symlink_to(target)
        result = self.cli()
        self.assertEqual(result.returncode, 1)
        self.assertFalse(json.loads(result.stderr)["certificateInstalled"])
        self.assertEqual(target.read_bytes(), self.certificates["old"])
        self.assertEqual(self.requests, [])

    def test_fixed_next_path_is_untouched(self):
        target = self.root / "untouched"
        target.write_bytes(b"unrelated-file")
        (self.directory / "object.next").symlink_to(target)
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(target.read_bytes(), b"unrelated-file")
        self.assertTrue((self.directory / "object.next").is_symlink())

    def test_local_inputs_changed_during_download_rejected(self):
        self.before_response = lambda: (self.directory / "server.env").write_text("OBJECT_BIND_ADDRESS=127.0.0.2\n")
        self.failed("local_inputs_changed")

    def test_replace_failure_does_not_modify_old_certificate(self):
        output, errors = io.StringIO(), io.StringIO()
        with mock.patch.object(receiver.os, "replace", side_effect=OSError("private-path-sentinel")), contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
            status = receiver.main(self.arguments())
        self.assertEqual(status, 1)
        self.assertEqual(output.getvalue(), "")
        self.assertFalse(json.loads(errors.getvalue())["certificateInstalled"])
        self.assertNotIn("private-path-sentinel", errors.getvalue())
        self.assertEqual((self.directory / "object.pem").read_bytes(), self.certificates["old"])
        self.no_temporary_files()

    def test_post_commit_cleanup_failure_reports_installed_state(self):
        output, errors = io.StringIO(), io.StringIO()
        with mock.patch.object(receiver.shutil, "rmtree", side_effect=OSError("private-path-sentinel")), contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors):
            status = receiver.main(self.arguments())
        self.assertEqual(status, 1)
        self.assertEqual(output.getvalue(), "")
        report = json.loads(errors.getvalue())
        self.assertEqual(report["code"], "certificate_temporary_cleanup_failed")
        self.assertTrue(report["certificateInstalled"])
        self.assertEqual((self.directory / "object.pem").read_bytes(), self.certificates["new"])
        for path in self.directory.glob(".certificate-*"):
            if path.is_dir():
                shutil.rmtree(path)

    def test_installed_certificate_completes_real_verified_tls(self):
        result = self.cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        server_context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        server_context.load_cert_chain(self.directory / "object.pem", self.directory / "object.key")
        errors = []
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen(1)
            listener.settimeout(3)
            def serve():
                try:
                    with listener.accept()[0] as raw:
                        raw.settimeout(3)
                        with server_context.wrap_socket(raw, server_side=True) as tls:
                            tls.sendall(b"verified")
                except Exception as error:
                    errors.append(error)
            thread = threading.Thread(target=serve)
            thread.start()
            try:
                context = ssl.create_default_context(cafile=str(self.directory / "application-ca.pem"))
                with socket.create_connection(listener.getsockname(), timeout=3) as raw, context.wrap_socket(raw, server_hostname="127.0.0.1") as tls:
                    self.assertEqual(tls.recv(32), b"verified")
            finally:
                thread.join(5)
            self.assertFalse(thread.is_alive())
            self.assertEqual(errors, [])

    def test_invalid_primary_address_fails_without_network(self):
        result = subprocess.run([sys.executable, "-O", str(SCRIPT), "bad-address", "--directory", str(self.directory)],
                                capture_output=True, text=True, timeout=5)
        self.assertEqual(result.returncode, 2)
        self.assertEqual(self.requests, [])


if __name__ == "__main__":
    unittest.main()
