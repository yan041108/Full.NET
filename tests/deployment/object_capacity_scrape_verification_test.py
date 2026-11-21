"""只读容量验收工具的精确样本判定及真实回环 HTTP/TLS 边界。"""
import http.server
import importlib.util
import json
import os
from pathlib import Path
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.parse

ROOT = Path(__file__).resolve().parents[2]
PROGRAM = ROOT / 'eng/deploy/ubuntu/Verify-ObjectCapacityScrape.py'
SPEC = importlib.util.spec_from_file_location('capacity_scrape_verifier', PROGRAM)
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)
IDENTITY = {'job': 'fullnet-object-capacity', 'cluster': 'test-cluster', 'instance': 'storage.internal:9100'}


def samples(now, changes=None):
    values = {'up': 1, 'node_textfile_scrape_error': 0, verifier.SCRAPE_TIMESTAMP: now - 5,
        verifier.PREFIX + 'probe_success': 1, verifier.PREFIX + 'sample_timestamp_seconds': now - 300,
        verifier.PREFIX + 'growth_state': 0, verifier.PREFIX + 'free_slots': 24, verifier.PREFIX + 'free_bytes': 8 * 1024**3}
    values.update(changes or {})
    return [{'metric': {**IDENTITY, '__name__': name}, 'value': [now, str(value)]}
        for name, value in values.items() if value is not None]


class CapacityScrapeDecisionTests(unittest.TestCase):
    def test_capacity_states_keep_exit_codes_and_current_values(self):
        for number, name in ((0, 'growth_headroom_available'), (1, 'growth_at_risk'), (2, 'growth_blocked')):
            report, code = verifier.evaluate(samples(1000, {verifier.PREFIX + 'growth_state': number}), IDENTITY, 1000)
            self.assertEqual((report['state'], code), (name, 0 if number == 0 else 1))
            self.assertEqual((report['freeSlots'], report['sampleAgeSeconds']), (24, 300))

    def test_absent_and_down_target_do_not_expose_old_capacity(self):
        for points, state in (([], 'target_missing'), (samples(1000, {'up': 0}), 'target_down')):
            report, code = verifier.evaluate(points, IDENTITY, 1000)
            self.assertEqual((report['state'], code), (state, 2))
            self.assertNotIn('freeSlots', report)

    def test_textfile_failure_and_failed_probe_are_distinct(self):
        report, code = verifier.evaluate(samples(1000, {'node_textfile_scrape_error': 1}), IDENTITY, 1000)
        self.assertEqual((report['state'], code), ('textfile_unavailable', 2))
        changes = {verifier.PREFIX + 'probe_success': 0, **{verifier.PREFIX + name: None
            for name in ('growth_state', 'free_slots', 'free_bytes')}}
        report, code = verifier.evaluate(samples(1000, changes), IDENTITY, 1000)
        self.assertEqual((report['state'], code), ('probe_unavailable', 2))
        self.assertNotIn('freeBytes', report)
        with self.assertRaisesRegex(verifier.VerificationFailure, 'failed_probe_has_old_capacity'):
            verifier.evaluate(samples(1000, {verifier.PREFIX + 'probe_success': 0}), IDENTITY, 1000)

    def test_original_freshness_boundary_and_independent_scrape_time(self):
        for changes, state in (({verifier.PREFIX + 'sample_timestamp_seconds': 280}, 'capacity_sample_stale'),
                ({verifier.SCRAPE_TIMESTAMP: 939}, 'scrape_stale')):
            report, code = verifier.evaluate(samples(1000, changes), IDENTITY, 1000)
            self.assertEqual((report['state'], code), (state, 2))
            self.assertNotIn('freeBytes', report)
        for name in (verifier.SCRAPE_TIMESTAMP, verifier.PREFIX + 'sample_timestamp_seconds'):
            with self.subTest(name=name), self.assertRaises(verifier.VerificationFailure):
                verifier.evaluate(samples(1000, {name: 1061}), IDENTITY, 1000)

    def test_incomplete_nonfinite_and_fractional_samples_never_report_healthy(self):
        for name, value in ((verifier.PREFIX + 'free_slots', None), (verifier.PREFIX + 'growth_state', 3),
                (verifier.PREFIX + 'free_bytes', 'NaN'), ('up', 'Inf'),
                (verifier.PREFIX + 'free_slots', .5), (verifier.PREFIX + 'free_bytes', -1),
                (verifier.PREFIX + 'probe_success', 2), ('node_textfile_scrape_error', None)):
            with self.subTest(name=name, value=value), self.assertRaises(verifier.VerificationFailure):
                verifier.evaluate(samples(1000, {name: value}), IDENTITY, 1000)

    def test_duplicate_identity_mismatch_and_wrong_query_time_are_rejected(self):
        cases = [samples(1000) + samples(1000)[:1], samples(900)]
        for change in ({'cluster': 'foreign'}, {'extra': 'ambiguous'}, {'__name__': 'foreign_metric'}):
            points = samples(1000)
            points[0]['metric'].update(change)
            cases.append(points)
        for points in cases:
            with self.subTest(points=points[0]['metric']), self.assertRaises(verifier.VerificationFailure):
                verifier.evaluate(points, IDENTITY, 1000)

    def test_url_and_selector_reject_credentials_redirect_inputs_and_injection(self):
        for url in ('http://storage.internal:9090', 'https://user:secret@localhost', 'https://localhost/?token=secret',
                'https://localhost/#token', 'file:///etc/passwd', 'https://localhost:99999', 'https://local host'):
            with self.subTest(url=url), self.assertRaises((verifier.VerificationFailure, ValueError)):
                verifier.endpoint(url)
        with self.assertRaises(verifier.VerificationFailure):
            verifier.endpoint('http://127.0.0.1:9090', token_file=Path('/token'))
        for cluster, instance in (('test"}or vector(1)', 'storage:9100'), ('test', 'storage:0'),
                ('test', 'user@storage:9100'), ('test', 'storage:9100/path')):
            with self.subTest(cluster=cluster, instance=instance), self.assertRaises(verifier.VerificationFailure):
                verifier.labels(cluster, instance)


class CapacityScrapeHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.materials = tempfile.TemporaryDirectory(prefix='fullnet-capacity-query-')
        cls.addClassCleanup(cls.materials.cleanup)
        cls.root = Path(cls.materials.name)
        for name in ('server', 'untrusted'):
            result = subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost', '-addext', 'basicConstraints=critical,CA:TRUE',
                '-keyout', str(cls.root / (name + '.key')), '-out', str(cls.root / (name + '.pem'))], capture_output=True, timeout=15)
            if result.returncode:
                raise RuntimeError('临时证书生成失败')

    def setUp(self):
        self.mode = 'normal'
        self.requests = []
        self.required_token = None
        owner = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *_args):
                pass

            def do_GET(self):
                owner.requests.append(self.path)
                if owner.required_token and self.headers.get('Authorization') != 'Bearer ' + owner.required_token:
                    self.send_error(401)
                    return
                if owner.mode == 'redirect':
                    self.send_response(302)
                    self.send_header('Location', '/forbidden')
                    self.end_headers()
                    return
                query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query)
                now = float(query['time'][0])
                value = {'status': 'success', 'data': {'resultType': 'vector', 'result': samples(now)}}
                if owner.mode == 'warning':
                    value['warnings'] = ['fixture partial data']
                elif owner.mode == 'wrong_type':
                    value['data']['resultType'] = 'matrix'
                elif owner.mode == 'too_many':
                    value['data']['result'] *= 3
                content = b'x' * 65537 if owner.mode == 'oversized' else json.dumps(value).encode()
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(content)))
                self.end_headers()
                try:
                    if owner.mode == 'slow':
                        for byte in content:
                            self.wfile.write(bytes([byte]))
                            self.wfile.flush()
                            time.sleep(.2)
                    else:
                        self.wfile.write(content)
                except OSError:
                    pass
        self.server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.server.daemon_threads = True
        self.url = 'http://127.0.0.1:' + str(self.server.server_port)
        self.addCleanup(self.server.server_close)

    def start(self, tls=False):
        if tls:
            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.load_cert_chain(self.root / 'server.pem', self.root / 'server.key')
            self.server.socket = context.wrap_socket(self.server.socket, server_side=True)
            self.url = 'https://localhost:' + str(self.server.server_port)
        thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(thread.join, 2)
        self.addCleanup(self.server.shutdown)

    def cli(self, *extra):
        result = subprocess.run([sys.executable, '-O', str(PROGRAM), '--prometheus-url', self.url,
            '--cluster', IDENTITY['cluster'], '--instance', IDENTITY['instance'], *map(str, extra)],
            capture_output=True, text=True, timeout=12, env={**os.environ, 'HTTP_PROXY': 'http://127.0.0.1:1',
                'HTTPS_PROXY': 'http://127.0.0.1:1', 'NO_PROXY': ''})
        self.assertEqual(result.stderr, '')
        return result, json.loads(result.stdout)

    def test_actual_http_cli_uses_one_exact_query_without_proxy(self):
        self.start()
        result, report = self.cli()
        self.assertEqual((result.returncode, report['state']), (0, 'growth_headroom_available'))
        self.assertEqual(len(self.requests), 1)
        query = urllib.parse.parse_qs(urllib.parse.urlsplit(self.requests[0]).query)['query'][0]
        self.assertIn('timestamp(up{', query)
        self.assertIn('cluster="test-cluster"', query)

    def test_real_tls_requires_ca_hostname_and_private_bearer_file(self):
        self.required_token = 'fixture-only-token'
        token_path = self.root / 'token'
        token_path.write_text(self.required_token)
        token_path.chmod(0o600)
        self.start(tls=True)
        result, report = self.cli('--ca-file', self.root / 'server.pem', '--token-file', token_path)
        self.assertEqual((result.returncode, report['state']), (0, 'growth_headroom_available'))
        for extra in (('--ca-file', self.root / 'untrusted.pem', '--token-file', token_path),
                ('--ca-file', self.root / 'server.pem')):
            result, report = self.cli(*extra)
            self.assertEqual((result.returncode, report['code']), (2, 'api_unavailable'))
            self.assertNotIn(self.required_token, result.stdout)
        self.url = self.url.replace('localhost', '127.0.0.1')
        result, report = self.cli('--ca-file', self.root / 'server.pem', '--token-file', token_path)
        self.assertEqual((result.returncode, report['code']), (2, 'api_unavailable'))

    def test_redirect_partial_oversized_and_invalid_shapes_fail_closed(self):
        self.start()
        for mode in ('redirect', 'oversized', 'warning', 'wrong_type', 'too_many'):
            self.mode = mode
            before = len(self.requests)
            result, report = self.cli()
            self.assertEqual((result.returncode, report['state']), (2, 'verification_failed'))
            self.assertEqual(len(self.requests) - before, 1)

    def test_token_links_permissions_and_header_injection_are_rejected(self):
        path = self.root / 'unsafe-token'
        path.write_text('fixture-secret\nInjected: value')
        path.chmod(0o600)
        with self.assertRaises(verifier.VerificationFailure):
            verifier.token(path)
        path.write_text('fixture-secret')
        path.chmod(0o644)
        with self.assertRaises(verifier.VerificationFailure):
            verifier.token(path)
        link = self.root / 'token-link'
        link.symlink_to(path)
        try:
            with self.assertRaises(OSError):
                verifier.token(link)
        finally:
            link.unlink()

    def test_slow_stream_cannot_extend_the_cli_wall_clock_budget(self):
        self.mode = 'slow'
        self.start()
        started = time.monotonic()
        result, report = self.cli()
        self.assertEqual((result.returncode, report['code']), (2, 'verification_deadline_exceeded'))
        self.assertLess(time.monotonic() - started, 10)


if __name__ == '__main__':
    unittest.main()
