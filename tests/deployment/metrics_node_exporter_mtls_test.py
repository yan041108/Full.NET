"""现有容量 textfile 发布器→真实 Node Exporter→Prometheus mTLS，不接触宿主服务。"""
import importlib.util
import json
import shutil
import unittest
import urllib.parse

import metrics_platform_runtime_test as runtime
from metrics_platform_test import ROOT, bundle, configuration, deployments

NODE_EXPORTER = 'quay.io/prometheus/node-exporter:v1.13.1@sha256:011e2d12c5bff759b90804bda642664775d7b4b127bf5148539bb82d3f590bb7'


class NodeExporterMtlsTests(runtime.MetricsPlatformRuntimeFixture):
    components = ('prometheus',)

    def test_formal_capacity_textfile_reaches_real_exporter_and_prometheus_only_with_trusted_client(self):
        runtime.command(['docker', 'pull', NODE_EXPORTER], seconds=180)
        self.receiver = self.run_container('probe', 'python:3.12-slim-bookworm',
            ['python3', '-c', 'import time; time.sleep(3600)'])
        pki = self.root / 'pki'
        pki.mkdir()
        for name in ('trusted', 'untrusted'):
            runtime.command(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                '-subj', '/CN=' + name, '-addext', 'basicConstraints=critical,CA:TRUE',
                '-addext', 'keyUsage=critical,keyCertSign,cRLSign',
                '-keyout', pki / (name + '.key'), '-out', pki / (name + '.pem')], seconds=30)
        for name, issuer, purpose, san in (('server', 'trusted', 'serverAuth', 'node-exporter.observability.svc'),
                ('client', 'trusted', 'clientAuth', 'prometheus.observability.svc'),
                ('bad-issuer', 'untrusted', 'clientAuth', 'prometheus.observability.svc'),
                ('bad-san', 'trusted', 'clientAuth', 'other.observability.svc')):
            runtime.command(['openssl', 'req', '-new', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=' + name,
                '-keyout', pki / (name + '.key'), '-out', pki / (name + '.csr')], seconds=30)
            extension = pki / (name + '.ext')
            extension.write_text('basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\n'
                'extendedKeyUsage=' + purpose + '\nsubjectAltName=DNS:' + san + '\n')
            runtime.command(['openssl', 'x509', '-req', '-days', '1', '-in', pki / (name + '.csr'),
                '-CA', pki / (issuer + '.pem'), '-CAkey', pki / (issuer + '.key'), '-CAcreateserial',
                '-extfile', extension, '-out', pki / (name + '.pem')], seconds=30)
        # 私有临时父目录保持 0700；容器仅获得所需叶子凭据的只读挂载，不挂载 CA 私钥。
        secrets = self.root / 'secrets'
        secrets.mkdir(mode=0o755)
        for name, source in (('exporter-client', 'client'), ('exporter-bad-issuer', 'bad-issuer'), ('exporter-bad-san', 'bad-san')):
            directory = secrets / name
            directory.mkdir(mode=0o755)
            for key, suffix in (('tls.crt', '.pem'), ('tls.key', '.key')):
                shutil.copyfile(pki / (source + suffix), directory / key)
                (directory / key).chmod(0o644)
        ca = secrets / 'exporter-ca'
        ca.mkdir(mode=0o755)
        shutil.copyfile(pki / 'trusted.pem', ca / 'ca.pem')
        (ca / 'ca.pem').chmod(0o644)
        exporter = self.root / 'exporter'
        exporter.mkdir(mode=0o755)
        for directory in ('server', 'client-ca'):
            (exporter / directory).mkdir(mode=0o755)
        for source, target in (('server.pem', 'server/tls.crt'), ('server.key', 'server/tls.key'), ('trusted.pem', 'client-ca/ca.pem')):
            shutil.copyfile(pki / source, exporter / target)
            (exporter / target).chmod(0o644)
        shutil.copyfile(ROOT / 'deploy/observability/examples/node-exporter-web.json', exporter / 'web.json')
        (exporter / 'web.json').chmod(0o644)
        metrics = self.root / 'textfile'
        metrics.mkdir(mode=0o755)
        specification = importlib.util.spec_from_file_location('capacity_fixture_publisher', ROOT / 'eng/deploy/ubuntu/Verify-ObjectStorageCapacity.py')
        publisher = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(publisher)
        publisher.publish_metrics(metrics, {'state': 'growth_headroom_available', 'freeSlots': 24, 'freeBytes': 8 * 1024**3})
        node_exporter = self.run_container('node-exporter', NODE_EXPORTER,
            ['--web.listen-address=:9100', '--web.config.file=/etc/node-exporter/web.json',
                '--collector.disable-defaults', '--collector.textfile', '--collector.textfile.directory=/textfile'],
            mounts=[(exporter, '/etc/node-exporter'), (metrics, '/textfile')], alias='node-exporter.observability.svc')
        sources = []
        for job, client in (('fullnet-object-capacity', 'exporter-client'), ('waf', None),
                ('fluent-bit', 'exporter-bad-issuer'), ('ddos', 'exporter-bad-san')):
            source = {'job': job, 'targets': ['node-exporter.observability.svc:9100'], 'caSecretName': 'exporter-ca'}
            if client:
                source['clientCertificateSecretName'] = client
            sources.append(source)
        value = bundle('--set-json', 'externalScrapes=' + json.dumps(sources))
        configs = configuration(value)
        for name, content in configs.items():
            (self.config_directory / name).write_text(json.dumps(content))
        prom = deployments(value)['prometheus']['spec']['template']['spec']
        secret_names = {volume['name']: volume['secret']['secretName'] for volume in prom['volumes'] if 'secret' in volume}
        mounts = [(self.config_directory, '/etc/fullnet')]
        for mount in prom['containers'][0]['volumeMounts']:
            if mount['name'] in secret_names:
                mounts.append((secrets / secret_names[mount['name']], mount['mountPath']))
        self.run_container('prometheus', prom['containers'][0]['image'], prom['containers'][0]['args'], mounts=mounts,
            tmpfs=['/prometheus:uid=65534,gid=65534,mode=0700,size=64m'], alias='prometheus.observability.svc')
        def query(expression):
            return self.request('http://prometheus.observability.svc:9090/api/v1/query?' +
                urllib.parse.urlencode({'query': expression})).get('data', {}).get('result', [])
        selector = '{job="fullnet-object-capacity",cluster="test-cluster",instance="node-exporter.observability.svc:9100"}'
        def value_is(name, expected):
            points = query(name + selector)
            return len(points) == 1 and float(points[0]['value'][1]) == expected
        self.wait(lambda: value_is('up', 1) and value_is('node_textfile_scrape_error', 0))
        self.wait(lambda: value_is('fullnet_object_storage_capacity_growth_state', 0)
            and bool(query('fullnet:object_storage_capacity_sample_fresh' + selector)))
        info = query('node_exporter_build_info' + selector)
        self.assertEqual(info[0]['metric']['version'], '1.13.1')
        for job in ('waf', 'fluent-bit', 'ddos'):
            self.wait(lambda: any(float(item['value'][1]) == 0 for item in query('up{job="' + job + '"}')))
            self.assertEqual(query('fullnet_object_storage_capacity_probe_success{job="' + job + '"}'), [])
        targets = self.request('http://prometheus.observability.svc:9090/api/v1/targets')['data']['activeTargets']
        denied = [target for target in targets if target['labels']['job'] in ('waf', 'fluent-bit', 'ddos')]
        self.assertEqual(len(denied), 3)
        errors = {target['labels']['job']: target['lastError'] for target in denied}
        self.assertTrue(all(target['health'] == 'down' and target['lastError'] for target in denied), errors)
        # Go 客户端可将对端握手拒绝报告为 EOF；用真实服务端拒绝原因核对 TLS 边界。
        log_result = runtime.subprocess.run(['docker', 'logs', '--tail', '40', node_exporter],
            capture_output=True, text=True, timeout=10)
        self.assertEqual(log_result.returncode, 0)
        exporter_logs = log_result.stdout + log_result.stderr
        self.assertIn('TLS handshake error', exporter_logs)
        self.assertIn("client didn't provide a certificate", exporter_logs)
        self.assertIn('could not find allowed SANs in client cert', exporter_logs)
        print('node_exporter_mtls_rejections: ' + json.dumps(errors, sort_keys=True), flush=True)
        publisher.publish_metrics(metrics, {'state': 'growth_blocked', 'freeSlots': 0, 'freeBytes': 8 * 1024**3})
        self.wait(lambda: value_is('fullnet_object_storage_capacity_growth_state', 2))
        publisher.publish_metrics(metrics, {'state': 'probe_unavailable'})
        self.wait(lambda: value_is('fullnet_object_storage_capacity_probe_success', 0)
            and not query('fullnet:object_storage_capacity_sample_fresh' + selector) and value_is('up', 1))
        self.assertEqual(query('fullnet_object_storage_capacity_growth_state' + selector), [])
        print('node_exporter_mtls: real_exporter=1.13.1 textfile=available->blocked->unavailable trusted_client=accepted missing_untrusted_wrong_san=denied scrape_up_when_probe_fails=1', flush=True)


if __name__ == '__main__':
    unittest.main()
