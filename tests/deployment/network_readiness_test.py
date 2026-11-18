"""只运行回环协议服务；不连接发布环境，不发送数据库认证或业务命令。"""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import socket
import ssl
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "eng/deploy/ubuntu/Verify-Network.py"
spec = importlib.util.spec_from_file_location("fullnet_network", SCRIPT)
network = importlib.util.module_from_spec(spec)
spec.loader.exec_module(network)


def exact(connection, count):
    result = bytearray()
    while len(result) < count:
        part = connection.recv(count - len(result))
        if not part:
            raise RuntimeError("fixture_client_disconnected")
        result.extend(part)
    return bytes(result)


def packet(payload, sequence=0):
    return len(payload).to_bytes(3, "little") + bytes([sequence]) + payload


def greeting(flags=512 | 2048 | 32768):
    return (b"\x0a8.0-test\0" + struct.pack("<I", 123) + b"12345678\0"
            + struct.pack("<H", flags & 65535) + b"\xff\x02\0"
            + struct.pack("<H", flags >> 16) + b"\0" * 11 + b"abcdefghijkl\0")


def fragmented(connection, data):
    # 延迟首字节与 CRLF 尾字节，强制跨多次读取；不假设 TCP 保留发送边界。
    for part in (data[:1], data[1:3], data[3:-1], data[-1:]):
        if part:
            connection.sendall(part)
            time.sleep(0.01)


class LoopbackServer:
    def __init__(self, handler):
        self.handler = handler
        self.errors = []
        self.listener = socket.socket()
        self.listener.bind(("127.0.0.1", 0))
        self.listener.listen(1)
        self.listener.settimeout(4)
        self.port = self.listener.getsockname()[1]
        self.thread = threading.Thread(target=self.run, daemon=False)

    def run(self):
        try:
            with self.listener.accept()[0] as connection:
                connection.settimeout(3)
                self.handler(connection)
        except Exception as error:
            self.errors.append(error)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.thread.join(5)
        self.listener.close()
        if self.thread.is_alive():
            raise AssertionError("fixture_thread_did_not_exit")
        if self.errors:
            raise AssertionError("fixture_server_failed") from self.errors[0]


class NetworkReadinessTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix="fullnet-network-")
        cls.addClassCleanup(cls.temp.cleanup)
        root = Path(cls.temp.name)
        cls.certificates = {}
        for name, san in (("valid", "DNS:localhost,IP:127.0.0.1"), ("wrong", "DNS:elsewhere.invalid")):
            cert, key = root / f"{name}.pem", root / f"{name}.key"
            subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
                            "-keyout", str(key), "-out", str(cert), "-days", "1",
                            "-subj", f"/CN={name}", "-addext", f"subjectAltName={san}"],
                           check=True, capture_output=True, timeout=20)
            key.chmod(0o600)
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.minimum_version = ssl.TLSVersion.TLSv1_2
            context.load_cert_chain(cert, key)
            cls.certificates[name] = (cert, context)

    def cli(self, redis_port, *extra):
        return subprocess.run([sys.executable, "-O", str(SCRIPT), "--redis", "127.0.0.1",
                               "--redis-port", str(redis_port), *map(str, extra)],
                              capture_output=True, text=True, timeout=8)

    def redis(self, response=b"-NOAUTH Authentication required.\r\n", split=False):
        def serve(connection):
            self.assertEqual(exact(connection, 14), b"*1\r\n$4\r\nPING\r\n")
            if split:
                fragmented(connection, response)
            else:
                connection.sendall(response)
        return LoopbackServer(serve)

    def failed(self, result, code):
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, "")
        self.assertEqual(json.loads(result.stderr), {"status": "failed", "code": code})

    def mysql(self, payload=None, frame=None, tls_name=None, rejected=False):
        state = {}
        def serve(connection):
            data = frame if frame is not None else packet(greeting() if payload is None else payload)
            if tls_name:
                fragmented(connection, data)
            else:
                connection.sendall(data)
            if tls_name:
                header = exact(connection, 4)
                self.assertEqual(header, b"\x20\0\0\x01")
                request = exact(connection, 32)
                flags, maximum, charset, filler = struct.unpack("<IIB23s", request)
                self.assertEqual(flags, 512 | 2048 | 32768)
                self.assertEqual((maximum, charset, filler), (16777216, 255, b"\0" * 23))
                state["request"] = True
                try:
                    with self.certificates[tls_name][1].wrap_socket(connection, server_side=True) as tls:
                        self.assertFalse(rejected, "untrusted TLS unexpectedly accepted")
                        # TLS 完成即断开；不能出现用户名、密码或后续 MySQL 包。
                        self.assertEqual(tls.recv(1), b"")
                        state["tls"] = True
                except ssl.SSLError:
                    if not rejected:
                        raise
                    state["rejected"] = True
        return LoopbackServer(serve), state

    def combined(self, server, ca):
        with self.redis() as redis, server:
            return self.cli(redis.port, "--mysql", "127.0.0.1", "--mysql-port", server.port, "--ca", ca)

    def test_redis_fragmented_noauth(self):
        with self.redis(split=True) as server:
            result = self.cli(server.port)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), {"status": "passed", "redis": {"anonymousPingRejected": True}})
        self.assertEqual(result.stderr, "")

    def test_redis_exact_error_token(self):
        with self.redis(b"-NOAUTH\r\n") as server:
            self.assertEqual(self.cli(server.port).returncode, 0)

    def test_redis_anonymous_pong_rejected_under_optimization(self):
        with self.redis(b"+PONG\r\n") as server:
            self.failed(self.cli(server.port), "redis_anonymous_ping_not_denied")

    def test_redis_lookalike_error_rejected(self):
        with self.redis(b"-NOAUTHFAKE private-secret\r\n") as server:
            result = self.cli(server.port)
        self.failed(result, "redis_anonymous_ping_not_denied")
        self.assertNotIn("private-secret", result.stderr)

    def test_redis_other_error_rejected(self):
        with self.redis(b"-ERR private-secret\r\n") as server:
            self.failed(self.cli(server.port), "redis_anonymous_ping_not_denied")

    def test_redis_truncated_reply(self):
        with self.redis(b"-NOAUTH unfinished") as server:
            self.failed(self.cli(server.port), "protocol_truncated")

    def test_redis_oversized_reply(self):
        with self.redis(b"-NOAUTH " + b"x" * 1016) as server:
            self.failed(self.cli(server.port), "redis_reply_too_large")

    def test_redis_read_deadline(self):
        def serve(connection):
            exact(connection, 14)
            connection.sendall(b"-")
            time.sleep(0.2)
        with LoopbackServer(serve) as server:
            self.failed(self.cli(server.port, "--timeout", ".05"), "network_timeout")

    def test_mysql_fragmented_frame_real_tls_and_ssl_request(self):
        server, state = self.mysql(tls_name="valid")
        result = self.combined(server, self.certificates["valid"][0])
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["status"], "passed")
        self.assertTrue(report["mysql"]["certificateIdentityVerified"])
        self.assertIn(report["mysql"]["tlsVersion"], ("TLSv1.2", "TLSv1.3"))
        self.assertEqual(state, {"request": True, "tls": True})

    def test_mysql_wrong_ca_rejected(self):
        server, state = self.mysql(tls_name="valid", rejected=True)
        self.failed(self.combined(server, self.certificates["wrong"][0]), "mysql_certificate_verification_failed")
        self.assertTrue(state["rejected"])

    def test_mysql_wrong_ip_identity_rejected(self):
        server, state = self.mysql(tls_name="wrong", rejected=True)
        self.failed(self.combined(server, self.certificates["wrong"][0]), "mysql_certificate_verification_failed")
        self.assertTrue(state["rejected"])

    def test_mysql_missing_tls_capability(self):
        server, _ = self.mysql(payload=greeting(512 | 32768))
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_tls_not_advertised")

    def test_mysql_missing_protocol41_capability(self):
        server, _ = self.mysql(payload=greeting(2048 | 32768))
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_tls_not_advertised")

    def test_mysql_unsupported_protocol(self):
        server, _ = self.mysql(payload=b"\x09" + b"x" * 40)
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_protocol_unsupported")

    def test_mysql_short_greeting(self):
        server, _ = self.mysql(payload=b"\x0a8.0\0" + b"x" * 20)
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_greeting_invalid")

    def test_mysql_invalid_filler(self):
        payload = bytearray(greeting())
        payload[payload.index(0, 1) + 1 + 12] = 1
        server, _ = self.mysql(payload=payload)
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_greeting_invalid")

    def test_mysql_frame_sequence_rejected(self):
        server, _ = self.mysql(frame=packet(greeting(), 2))
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_frame_invalid")

    def test_mysql_oversized_frame_rejected_before_body(self):
        server, _ = self.mysql(frame=b"\x01\x00\x01\x00")
        self.failed(self.combined(server, self.certificates["valid"][0]), "mysql_frame_invalid")

    def test_mysql_truncated_header(self):
        server, _ = self.mysql(frame=b"\x20\0")
        self.failed(self.combined(server, self.certificates["valid"][0]), "protocol_truncated")

    def test_mysql_truncated_body(self):
        server, _ = self.mysql(frame=b"\x20\0\0\0\x0a8")
        self.failed(self.combined(server, self.certificates["valid"][0]), "protocol_truncated")

    def test_mysql_tls_stall_is_bounded(self):
        def serve(connection):
            connection.sendall(packet(greeting()))
            exact(connection, 36)
            time.sleep(.3)
        with self.redis() as redis, LoopbackServer(serve) as server:
            result = self.cli(redis.port, "--mysql", "127.0.0.1", "--mysql-port", server.port,
                              "--ca", self.certificates["valid"][0], "--timeout", ".1")
        self.failed(result, "network_timeout")

    def test_invalid_ca_fails_before_any_connection(self):
        with mock.patch.object(network.socket, "create_connection", side_effect=AssertionError("must_not_connect")):
            output, error = io.StringIO(), io.StringIO()
            with contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
                status = network.main(["--redis", "127.0.0.1", "--mysql", "127.0.0.1",
                                       "--ca", str(Path(self.temp.name) / "missing-private-ca")])
        self.assertEqual(status, 1)
        self.assertEqual(output.getvalue(), "")
        self.assertEqual(json.loads(error.getvalue())["code"], "network_or_ca_unavailable")
        self.assertNotIn("missing-private-ca", error.getvalue())

    def test_invalid_arguments_fail_before_connections(self):
        variants = (["--mysql", "localhost"], ["--ca", "unused"], ["--mysql-port", "3306"],
                    ["--redis-port", "0"], ["--redis-port", "65536"], ["--redis-port", "abc"],
                    ["--timeout", "nan"], ["--timeout", "inf"], ["--timeout", "0"],
                    ["--timeout", "31"], ["--redis", "https://private-secret"],
                    ["--redis", "bad host"])
        with mock.patch.object(network.socket, "create_connection", side_effect=AssertionError("must_not_connect")):
            for variant in variants:
                with self.subTest(variant=variant), contextlib.redirect_stderr(io.StringIO()):
                    with self.assertRaises(SystemExit) as result:
                        network.main(["--redis", "localhost", *variant])
                    self.assertEqual(result.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
