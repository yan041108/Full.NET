"""固定镜像真实 OTLP/Prometheus/Alertmanager/TLS 回路，只通知本轮私有接收器。"""
import concurrent.futures
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import unittest
import uuid

from metrics_platform_test import bundle, configuration, deployments

RECEIVER = r'''
import http.server,json,pathlib,ssl
root=pathlib.Path('/fixture')
class Handler(http.server.BaseHTTPRequestHandler):
 def do_POST(self):
  if self.headers.get('Authorization')!='Bearer fixture-only-token':self.send_error(401);return
  size=int(self.headers.get('Content-Length','0'))
  if not 0<size<=65536:self.send_error(400);return
  body=json.loads(self.rfile.read(size))
  events=json.loads((root/'events.json').read_text())
  for alert in body.get('alerts',[]):
   if alert.get('labels',{}).get('alertname')=='FullNetTelemetryPipelineProbe':
    events.append({'status':alert['status'],'fingerprint':alert['fingerprint']})
  (root/'events.json').write_text(json.dumps(events))
  self.send_response(200);self.end_headers()
 def log_message(self,*_):pass
server=http.server.HTTPServer(('0.0.0.0',8443),Handler)
context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(root/'server.pem',root/'server.key')
server.socket=context.wrap_socket(server.socket,server_side=True)
server.serve_forever()
'''


def command(arguments, seconds=120):
    result = subprocess.run(list(map(str, arguments)), capture_output=True, text=True, timeout=seconds)
    if result.returncode:
        raise RuntimeError(str(arguments[0]) + '_failed: ' + (result.stdout + result.stderr)[-2000:])
    return result.stdout.strip()


class MetricsPlatformRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(os.environ.get('FULLNET_METRICS_REAL') == 'isolated')
        context = json.loads(command(['docker', 'context', 'inspect']))[0]
        self.assertTrue(context['Endpoints']['docker']['Host'].startswith('unix://'))
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-metrics-runtime-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.network = 'fullnet-metrics-' + uuid.uuid4().hex[:12]
        self.containers = []
        command(['docker', 'network', 'create', '--internal', self.network])
        self.addCleanup(self.cleanup_runtime)
        self.value = bundle('--set', 'probeAlert=true,evaluationInterval=1s,receiver.enabled=true,receiver.caSecretName=fixture-ca,receiver.authorizationSecretName=fixture-token',
                            '--set-string', 'receiver.url=https://receiver.observability.svc:8443/alerts')
        self.configs = configuration(self.value)
        self.definitions = deployments(self.value)
        self.config_directory = self.root / 'configuration'
        self.config_directory.mkdir()
        for name, value in self.configs.items():
            path = self.config_directory / name
            path.write_text(json.dumps(value))
            path.chmod(0o644)
        images = [value['spec']['template']['spec']['containers'][0]['image'] for value in self.definitions.values()]
        images.append('python:3.12-slim-bookworm')
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            list(executor.map(lambda image: command(['docker', 'pull', image], seconds=180), images))

    def cleanup_runtime(self):
        failures = []
        for name in reversed(self.containers):
            info = subprocess.run(['docker', 'inspect', name], capture_output=True, text=True, timeout=10)
            if info.returncode:
                continue
            labels = json.loads(info.stdout)[0]['Config'].get('Labels') or {}
            if labels.get('fullnet.metrics.test') != self.network:
                failures.append('container_identity_changed')
                continue
            try:
                command(['docker', 'rm', '-f', name], seconds=30)
            except RuntimeError:
                failures.append('container_cleanup_failed')
        try:
            command(['docker', 'network', 'rm', self.network], seconds=30)
        except RuntimeError:
            failures.append('network_cleanup_failed')
        self.assertEqual(failures, [])
        remaining = command(['docker', 'ps', '-aq', '--filter', 'label=fullnet.metrics.test=' + self.network])
        self.assertEqual(remaining, '')
        print('metrics_platform_cleanup: own_containers=0 network_removed=true', flush=True)

    def run_container(self, label, image, arguments, *, mounts=(), tmpfs=(), uid='65534:65534', alias=None):
        name = self.network + '-' + label
        args = ['docker', 'run', '-d', '--name', name, '--label', 'fullnet.metrics.test=' + self.network,
                '--network', self.network, '--cpus', '0.5', '--memory', '256m', '--pids-limit', '128',
                '--read-only', '--cap-drop=ALL', '--security-opt=no-new-privileges', '--user', uid]
        if alias:
            args += ['--network-alias', alias]
        for source, target in mounts:
            args += ['--mount', f'type=bind,source={source},target={target},readonly']
        for target in tmpfs:
            args += ['--tmpfs', target]
        # 先登记唯一名称，创建响应丢失也只回收本轮可能已创建的容器。
        self.containers.append(name)
        command([*args, image, *arguments], seconds=45)
        return name

    def request(self, url, payload=None):
        # internal 网络不发布宿主端口；探针也在本轮接收器内执行，保持全程私有。
        code = ('import json,sys,urllib.request; value=json.load(sys.stdin); '
            'data=json.dumps(value["payload"]).encode() if value["payload"] is not None else None; '
            'call=urllib.request.Request(value["url"],data=data,headers={"Content-Type":"application/json"}); '
            'response=urllib.request.build_opener(urllib.request.ProxyHandler({})).open(call,timeout=3); '
            'body=response.read(1048576); print(body.decode() if body else "{}")')
        result = subprocess.run(['docker', 'exec', '-i', self.receiver, 'python3', '-c', code],
            input=json.dumps({'url': url, 'payload': payload}), capture_output=True, text=True, timeout=8)
        if result.returncode:
            raise OSError('fixture_http_not_ready')
        return json.loads(result.stdout)

    def wait(self, action, seconds=45):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            try:
                result = action()
                if result:
                    return result
            except (OSError, ValueError):
                pass
            time.sleep(0.5)
        logs = []
        for name in self.containers:
            result = subprocess.run(['docker', 'logs', '--tail', '12', name], capture_output=True, text=True, timeout=10)
            logs.append((result.stdout + result.stderr)[-1500:])
        self.fail('本轮指标回路未满足有界条件：' + repr(logs))

    def test_rendered_images_validate_and_complete_metrics_alert_tls_loop(self):
        for name, entrypoint, args in (
            ('prometheus', '/bin/promtool', ['check', 'config', '/etc/fullnet/prometheus.json']),
            ('alertmanager', '/bin/amtool', ['check-config', '/etc/fullnet/alertmanager.json']),
            ('telemetry-collector', None, ['validate', '--config=/etc/fullnet/collector.json']),
            ('kube-state-metrics', None, [*self.definitions['kube-state-metrics']['spec']['template']['spec']['containers'][0]['args'], '--help'])):
            # 通知 CA/凭据由后续真实 TLS 夹具提供；此处 Alertmanager 先验证默认无外发配置。
            if name == 'alertmanager':
                default = configuration(bundle())['alertmanager.json']
                default_path = self.config_directory / 'alertmanager-default.json'
                default_path.write_text(json.dumps(default))
                default_path.chmod(0o644)
                args = ['check-config', '/etc/fullnet/alertmanager-default.json']
            image = self.definitions[name]['spec']['template']['spec']['containers'][0]['image']
            call = ['docker', 'run', '--rm', '--network', 'none', '--cpus', '0.5', '--memory', '256m',
                    '--mount', f'type=bind,source={self.config_directory},target=/etc/fullnet,readonly',
                    '--tmpfs', '/var/otel-collector/storage:uid=10001,gid=10001,mode=0700']
            if entrypoint:
                call += ['--entrypoint', entrypoint]
            command([*call, image, *args], seconds=30)
        fixture = self.root / 'receiver'
        fixture.mkdir()
        for name in ('ca', 'wrong-ca'):
            command(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1', '-subj', '/CN=' + name,
                '-keyout', fixture / (name + '.key'), '-out', fixture / (name + '.pem')])
        command(['openssl', 'req', '-new', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=receiver.observability.svc',
            '-keyout', fixture / 'server.key', '-out', fixture / 'server.csr'])
        (fixture / 'extensions').write_text('subjectAltName=DNS:receiver.observability.svc\nextendedKeyUsage=serverAuth\n')
        command(['openssl', 'x509', '-req', '-in', fixture / 'server.csr', '-CA', fixture / 'ca.pem', '-CAkey', fixture / 'ca.key',
            '-set_serial', '2', '-days', '1', '-extfile', fixture / 'extensions', '-out', fixture / 'server.pem'])
        (fixture / 'token').write_text('fixture-only-token')
        (fixture / 'token').chmod(0o644)
        (fixture / 'events.json').write_text('[]')
        # 接收器只输出状态/指纹，不记录 Authorization、注释或任意业务标签。
        receiver = self.network + '-receiver'
        self.receiver = receiver
        self.containers.append(receiver)
        command(['docker', 'run', '-d', '--name', receiver, '--label', 'fullnet.metrics.test=' + self.network,
            '--network', self.network, '--network-alias', 'receiver.observability.svc', '--cpus', '0.5', '--memory', '128m',
            '--cap-drop=ALL', '--security-opt=no-new-privileges', '--user', f'{os.getuid()}:{os.getgid()}',
            '--mount', f'type=bind,source={fixture},target=/fixture',
            'python:3.12-slim-bookworm', 'python3', '-c', RECEIVER])
        am = self.definitions['alertmanager']['spec']['template']['spec']['containers'][0]
        alertmanager = self.run_container('alertmanager', am['image'], am['args'],
            mounts=[(self.config_directory, '/etc/fullnet'), (fixture / 'ca.pem', '/etc/alert-ca/ca.pem'), (fixture / 'token', '/etc/alert-auth/token')],
            tmpfs=['/alertmanager:uid=65534,gid=65534,mode=0700'], alias='alertmanager.observability.svc')
        prom = self.definitions['prometheus']['spec']['template']['spec']['containers'][0]
        prometheus = self.run_container('prometheus', prom['image'], prom['args'], mounts=[(self.config_directory, '/etc/fullnet')],
            tmpfs=['/prometheus:uid=65534,gid=65534,mode=0700,size=64m'], alias='prometheus.observability.svc')
        otel = self.definitions['telemetry-collector']['spec']['template']['spec']['containers'][0]
        collector = self.run_container('collector', otel['image'], otel['args'], uid='10001:10001',
            mounts=[(self.config_directory, '/etc/fullnet')], tmpfs=['/var/otel-collector/storage:uid=10001,gid=10001,mode=0700,size=64m'],
            alias='telemetry-collector.observability.svc')
        query = 'http://prometheus.observability.svc:9090/api/v1/query?query='
        ingest = 'http://telemetry-collector.observability.svc:4318/v1/metrics'
        def send(value):
            attributes = [{'key': key, 'value': {'stringValue': 'fixture-identity-sentinel'}} for key in ('tenant_id', 'user_id', 'DiagnosticGroup')]
            resource = [{'key': 'service.name', 'value': {'stringValue': 'fullnet-worker'}},
                        {'key': 'service.version', 'value': {'stringValue': 'isolated-fixture'}},
                        {'key': 'service.instance.id', 'value': {'stringValue': self.network}}, *attributes]
            result = self.request(ingest, {'resourceMetrics': [{'resource': {'attributes': resource}, 'scopeMetrics': [{'scope': {'name': 'Full.NET.PipelineProbe'},
                'metrics': [{'name': 'fullnet_pipeline_probe_state', 'gauge': {'dataPoints': [{'asDouble': value, 'timeUnixNano': str(time.time_ns()), 'attributes': attributes}]}}]}]}]})
            self.assertEqual(result.get('partialSuccess', {}).get('rejectedDataPoints', 0), 0)
            return True
        visibility_start = time.monotonic()
        self.wait(lambda: send(0))
        self.wait(lambda: self.request(query + 'fullnet_pipeline_probe_state').get('data', {}).get('result'))
        visibility_ms = round((time.monotonic() - visibility_start) * 1000, 3)
        send(1)
        series = self.wait(lambda: (result if result and result[0]['value'][1] == '1' else None)
            if (result := self.request(query + 'fullnet_pipeline_probe_state').get('data', {}).get('result')) else None)
        self.assertEqual(series[0]['metric']['job'], 'fullnet-worker')
        self.assertEqual(series[0]['metric']['instance'], self.network)
        self.assertNotIn('fixture-identity-sentinel', json.dumps(series))
        info = self.request(query + 'target_info').get('data', {}).get('result', [])
        self.assertTrue(info)
        self.assertTrue(any(value['metric'].get('service_version') == 'isolated-fixture' for value in info))
        self.assertNotIn('fixture-identity-sentinel', json.dumps(info))
        events = lambda: json.loads((fixture / 'events.json').read_text())
        firing = self.wait(lambda: next((event for event in events() if event['status'] == 'firing'), None), seconds=60)
        send(0)
        resolved = self.wait(lambda: next((event for event in events() if event['status'] == 'resolved'), None), seconds=60)
        self.assertEqual(firing['fingerprint'], resolved['fingerprint'])
        command(['docker', 'stop', '--time', '5', alertmanager], seconds=15)
        baseline = events()
        # 独立的错误 CA 对照直接投递合成告警，只验证 TLS 拒绝，不冒充 Prometheus 规则链。
        bad = self.run_container('wrong-ca', am['image'], am['args'],
            mounts=[(self.config_directory, '/etc/fullnet'), (fixture / 'wrong-ca.pem', '/etc/alert-ca/ca.pem'), (fixture / 'token', '/etc/alert-auth/token')],
            tmpfs=['/alertmanager:uid=65534,gid=65534,mode=0700'], alias='wrong-ca.observability.svc')
        target = 'http://wrong-ca.observability.svc:9093/api/v2/alerts'
        self.wait(lambda: self.request(target, [{'labels': {'alertname': 'FullNetTelemetryPipelineProbe', 'job': 'wrong-ca-control'}}]) is not None)
        def tls_failed():
            result = subprocess.run(['docker', 'logs', '--tail', '20', bad], capture_output=True, text=True, timeout=10)
            return 'x509:' in (result.stdout + result.stderr)
        self.wait(tls_failed, seconds=30)
        self.assertEqual(events(), baseline)
        print('metrics_platform_live_receipt: ' + json.dumps({'otlpAccepted': True, 'prometheusReadback': True,
            'initialSinglePointVisibilityMilliseconds': visibility_ms,
            'identityLabelsRemoved': True, 'tlsFiringResolvedSameFingerprint': True, 'wrongCaRejected': True,
            'receiver': 'self-owned-isolated-fixture', 'productionNotificationVerified': False}), flush=True)


if __name__ == '__main__':
    unittest.main()
