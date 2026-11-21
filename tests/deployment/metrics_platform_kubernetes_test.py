"""用原始平台清单验收 Kubernetes watch、存储权限和 Pod 重建；不连接现网。"""
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.parse
import uuid

from cluster_network_test import NODE_IMAGE, real_command
from metrics_platform_test import bundle, deployments


SCRAPE_SERVER = r'''
import http.server, pathlib, ssl, time
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        expected = pathlib.Path('/auth/token').read_text().strip()
        if self.headers.get('Authorization') != 'Bearer ' + expected:
            self.send_error(401); return
        if self.path != '/metrics':
            self.send_response(302); self.send_header('Location', '/metrics'); self.end_headers(); return
        body = ('# TYPE fullnet_object_storage_capacity_probe_success gauge\n'
            'fullnet_object_storage_capacity_probe_success 1\n'
            '# TYPE fullnet_object_storage_capacity_sample_timestamp_seconds gauge\n'
            'fullnet_object_storage_capacity_sample_timestamp_seconds ' + str(time.time()) + '\n'
            '# TYPE fullnet_object_storage_capacity_growth_state gauge\n'
            'fullnet_object_storage_capacity_growth_state 0\n').encode()
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain; version=0.0.4')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers(); self.wfile.write(body)
    def log_message(self, *_): pass
server = http.server.HTTPServer(('0.0.0.0', 8443), Handler)
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain('/certificate/cert.pem', '/certificate/key.pem')
server.socket = context.wrap_socket(server.socket, server_side=True)
server.serve_forever()
'''


class MetricsPlatformKubernetesTests(unittest.TestCase):
    def command(self, arguments, seconds=180):
        return real_command(arguments, self.environment, seconds).strip()

    def kubectl(self, *arguments, seconds=180):
        return self.command(['kubectl', '--context', 'kind-' + self.name, *arguments], seconds)

    def apply(self, document):
        result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, 'apply', '-f', '-'],
            input=json.dumps(document), env=self.environment, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, (result.stdout + result.stderr)[-3000:])

    def setUp(self):
        self.assertTrue(sys.platform.startswith('linux'))
        self.assertEqual(os.environ.get('FULLNET_METRICS_KUBERNETES'), 'isolated')
        context = json.loads(real_command(['docker', 'context', 'inspect']))[0]
        endpoint = context['Endpoints']['docker']['Host']
        self.assertTrue(endpoint.startswith('unix://'))
        self.assertIn(os.environ.get('DOCKER_HOST', endpoint), ('', endpoint))
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-metrics-kind-')
        self.root = Path(self.temporary.name)
        self.name = 'fullnet-metrics-' + uuid.uuid4().hex[:10]
        self.environment = {**os.environ, 'KUBECONFIG': str(self.root / 'kubeconfig')}
        self.addCleanup(self.temporary.cleanup)
        self.assertNotIn(self.name, self.command(['kind', 'get', 'clusters']).split())
        self.addCleanup(self.clean_cluster)
        configuration = self.root / 'kind.json'
        configuration.write_text(json.dumps({'kind': 'Cluster', 'apiVersion': 'kind.x-k8s.io/v1alpha4',
            'nodes': [{'role': 'control-plane'}, {'role': 'worker'}]}))
        print('metrics_kubernetes_stage: creating isolated cluster', flush=True)
        self.command(['kind', 'create', 'cluster', '--name', self.name, '--image', NODE_IMAGE,
            '--config', configuration, '--kubeconfig', self.environment['KUBECONFIG'], '--wait', '120s'], 240)

    def clean_cluster(self):
        nodes = self.command(['docker', 'ps', '-aq', '--filter', 'label=io.x-k8s.kind.cluster=' + self.name]).split()
        if nodes:
            for node in nodes:
                info = json.loads(self.command(['docker', 'inspect', node]))[0]
                self.assertEqual(info['Config']['Labels'].get('io.x-k8s.kind.cluster'), self.name)
            self.command(['kind', 'delete', 'cluster', '--name', self.name], 120)
        self.assertEqual(self.command(['docker', 'ps', '-aq', '--filter', 'label=io.x-k8s.kind.cluster=' + self.name]), '')
        print('metrics_kubernetes_cleanup: own_cluster_containers=0', flush=True)

    def http(self, url, payload=None):
        # 应用只发送 OTLP；查询在平台命名空间进行，保持与正式入口策略一致。
        code = ('import json,sys,urllib.request; v=json.load(sys.stdin); '
            'data=None if v["payload"] is None else json.dumps(v["payload"]).encode(); '
            'r=urllib.request.Request(v["url"],data=data,headers={"Content-Type":"application/json"}); '
            'response=urllib.request.build_opener(urllib.request.ProxyHandler({})).open(r,timeout=5); '
            'body=response.read(1048576); print(body.decode() if body else "{}")')
        application = url.startswith('http://telemetry-collector.observability.svc:4318/')
        namespace = 'fullnet-production' if application else 'observability'
        deployment = 'metrics-client' if application else 'metrics-observer'
        result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, '-n', namespace,
            'exec', '-i', 'deployment/' + deployment, '--', 'python3', '-c', code],
            input=json.dumps({'url': url, 'payload': payload}), env=self.environment,
            capture_output=True, text=True, timeout=15)
        if result.returncode:
            raise RuntimeError('isolated_http_failed: ' + result.stderr[-1000:])
        return json.loads(result.stdout)

    def query(self, expression):
        response = self.http('http://prometheus.observability.svc:9090/api/v1/query?' +
            urllib.parse.urlencode({'query': expression}))
        self.assertEqual(response['status'], 'success')
        return response['data']['result']

    def wait(self, action, seconds=60):
        deadline = time.monotonic() + seconds
        last = None
        while time.monotonic() < deadline:
            try:
                result = action()
                if result:
                    return result
            except RuntimeError as error:
                last = str(error)
            time.sleep(1)
        self.fail('isolated_condition_timeout: ' + str(last))

    def replace_pod(self, name):
        selector = 'app.kubernetes.io/name=' + name
        old = json.loads(self.kubectl('-n', 'observability', 'get', 'pods', '-l', selector, '-o', 'json'))['items']
        self.assertEqual(len(old), 1)
        old_uid = old[0]['metadata']['uid']
        self.kubectl('-n', 'observability', 'delete', 'pod', old[0]['metadata']['name'], '--wait=true', '--timeout=60s')
        self.kubectl('-n', 'observability', 'rollout', 'status', 'deployment/' + name, '--timeout=120s')
        new = json.loads(self.kubectl('-n', 'observability', 'get', 'pods', '-l', selector, '-o', 'json'))['items']
        self.assertEqual(len(new), 1)
        self.assertNotEqual(new[0]['metadata']['uid'], old_uid)

    def test_external_scrapes_use_projected_ca_and_token_and_reject_tls_auth_and_redirect_errors(self):
        target = 'metrics-source.observability.svc:8443'
        sources = [
            {'job': 'fullnet-object-capacity', 'targets': [target], 'caSecretName': 'exporter-ca',
                'authorizationSecretName': 'exporter-token'},
            {'job': 'waf', 'targets': [target], 'caSecretName': 'exporter-wrong-ca',
                'authorizationSecretName': 'exporter-token'},
            {'job': 'fluent-bit', 'targets': [target], 'caSecretName': 'exporter-ca',
                'authorizationSecretName': 'exporter-wrong-token'},
            {'job': 'ddos', 'targets': [target], 'caSecretName': 'exporter-ca',
                'authorizationSecretName': 'exporter-token', 'metricsPath': '/api/v1/metrics/prometheus'}]
        platform = bundle('--set-json', 'externalScrapes=' + json.dumps(sources))
        self.apply(next(item for item in platform['items'] if item['kind'] == 'Namespace'))
        for name in ('server', 'wrong-ca'):
            self.command(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                '-subj', '/CN=' + name, '-addext', 'subjectAltName=DNS:metrics-source.observability.svc',
                '-keyout', self.root / (name + '.key'), '-out', self.root / (name + '.pem')], 30)
        for name in ('token', 'wrong-token'):
            (self.root / name).write_text(uuid.uuid4().hex)
        for name, files in (
                ('exporter-ca', {'ca.pem': 'server.pem'}),
                ('exporter-wrong-ca', {'ca.pem': 'wrong-ca.pem'}),
                ('exporter-token', {'token': 'token'}), ('exporter-wrong-token', {'token': 'wrong-token'}),
                ('exporter-server', {'cert.pem': 'server.pem', 'key.pem': 'server.key'})):
            # 凭据只通过私有临时文件和 API Secret 传递，不进入参数、ConfigMap 或输出。
            self.kubectl('-n', 'observability', 'create', 'secret', 'generic', name,
                *['--from-file=' + key + '=' + str(self.root / file) for key, file in files.items()])
        self.apply({'apiVersion': 'v1', 'kind': 'PersistentVolumeClaim',
            'metadata': {'name': 'prometheus-data', 'namespace': 'observability'},
            'spec': {'accessModes': ['ReadWriteOnce'], 'storageClassName': 'standard',
                'resources': {'requests': {'storage': '1Gi'}}}})
        platform['items'] = [item for item in platform['items']
            if item['kind'] in ('Namespace', 'ConfigMap', 'NetworkPolicy') or
            (item['kind'] in ('Deployment', 'Service') and item['metadata']['name'] == 'prometheus')]
        self.apply(platform)
        self.apply({'apiVersion': 'v1', 'kind': 'ConfigMap',
            'metadata': {'name': 'metrics-source-program', 'namespace': 'observability'},
            'data': {'server.py': SCRAPE_SERVER}})
        for name in ('metrics-source', 'metrics-observer'):
            server = name == 'metrics-source'
            volumes = [
                {'name': 'program', 'configMap': {'name': 'metrics-source-program'}},
                {'name': 'certificate', 'secret': {'secretName': 'exporter-server', 'defaultMode': 288}},
                {'name': 'auth', 'secret': {'secretName': 'exporter-token', 'defaultMode': 288}}] if server else []
            mounts = [{'name': item['name'], 'mountPath': '/' + item['name'], 'readOnly': True} for item in volumes]
            self.apply({'apiVersion': 'apps/v1', 'kind': 'Deployment',
                'metadata': {'name': name, 'namespace': 'observability'},
                'spec': {'replicas': 1, 'selector': {'matchLabels': {'app': name}},
                    'template': {'metadata': {'labels': {'app': name}}, 'spec': {
                        'automountServiceAccountToken': False,
                        'securityContext': {'runAsNonRoot': True, 'runAsUser': 65534, 'fsGroup': 65534,
                            'seccompProfile': {'type': 'RuntimeDefault'}}, 'volumes': volumes,
                        'containers': [{'name': 'fixture', 'image': 'python:3.12-slim-bookworm',
                            'command': ['python3', '-u', '/program/server.py'] if server else
                                ['python3', '-c', 'import time; time.sleep(3600)'],
                            'volumeMounts': mounts,
                            'resources': {'requests': {'cpu': '50m', 'memory': '32Mi'},
                                'limits': {'cpu': '200m', 'memory': '128Mi'}},
                            'securityContext': {'readOnlyRootFilesystem': True, 'allowPrivilegeEscalation': False,
                                'capabilities': {'drop': ['ALL']}}}]}}}})
        self.apply({'apiVersion': 'v1', 'kind': 'Service',
            'metadata': {'name': 'metrics-source', 'namespace': 'observability'},
            'spec': {'selector': {'app': 'metrics-source'}, 'ports': [{'port': 8443, 'targetPort': 8443}]}})
        for name in ('metrics-source', 'metrics-observer', 'prometheus'):
            self.kubectl('-n', 'observability', 'rollout', 'status', 'deployment/' + name, '--timeout=180s', seconds=190)
        good = '{job="fullnet-object-capacity",cluster="test-cluster",instance="' + target + '"}'
        self.wait(lambda: any(float(v['value'][1]) == 1 for v in self.query('up' + good)))
        self.wait(lambda: bool(self.query('fullnet:object_storage_capacity_sample_fresh' + good)))
        self.assertEqual(float(self.query('fullnet_object_storage_capacity_growth_state' + good)[0]['value'][1]), 0)
        expected_errors = {'waf': 'x509', 'fluent-bit': '401', 'ddos': '302'}
        def rejected():
            targets = self.http('http://prometheus.observability.svc:9090/api/v1/targets')['data']['activeTargets']
            observed = {item['labels']['job']: item for item in targets if item['labels']['job'] in expected_errors}
            return len(observed) == len(expected_errors) and all(
                item['health'] == 'down' and expected_errors[name] in item['lastError']
                for name, item in observed.items())
        self.wait(rejected)
        for job in expected_errors:
            self.assertEqual(float(self.query('up{job="' + job + '"}')[0]['value'][1]), 0)
            self.assertEqual(self.query('fullnet_object_storage_capacity_probe_success{job="' + job + '"}'), [])
        print('external_scrapes_runtime: projected_ca_and_bearer=accepted capacity_freshness=1 wrong_ca=x509 wrong_token=401 redirect=302', flush=True)

    def test_hpa_and_pdb_changes_reach_prometheus_with_namespace_only_read_permissions(self):
        platform = bundle()
        self.kubectl('create', 'namespace', 'fullnet-production')
        self.apply(next(item for item in platform['items'] if item['kind'] == 'Namespace'))
        self.apply({'apiVersion': 'v1', 'kind': 'PersistentVolumeClaim',
            'metadata': {'name': 'prometheus-data', 'namespace': 'observability'},
            'spec': {'accessModes': ['ReadWriteOnce'], 'storageClassName': 'standard',
                'resources': {'requests': {'storage': '1Gi'}}}})
        # 本场景只需要 KSM 与 Prometheus，不重跑 Collector、通知或既有双卷恢复。
        platform['items'] = [item for item in platform['items'] if item['kind'] not in ('Deployment', 'Service')
            or item['metadata']['name'] in ('kube-state-metrics', 'prometheus')]
        self.apply(platform)
        security = {'runAsNonRoot': True, 'runAsUser': 65534, 'seccompProfile': {'type': 'RuntimeDefault'}}
        container = {'name': 'fixture', 'image': 'python:3.12-slim-bookworm',
            'command': ['python3', '-c', 'import time; time.sleep(3600)'],
            'resources': {'requests': {'cpu': '50m', 'memory': '32Mi'}, 'limits': {'cpu': '200m', 'memory': '128Mi'}},
            'securityContext': {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True,
                'capabilities': {'drop': ['ALL']}}}
        for name, namespace, replicas in (('resource-client', 'fullnet-production', 2),
                ('metrics-observer', 'observability', 1)):
            self.apply({'apiVersion': 'apps/v1', 'kind': 'Deployment',
                'metadata': {'name': name, 'namespace': namespace},
                'spec': {'replicas': replicas, 'selector': {'matchLabels': {'app': name}}, 'template': {
                    'metadata': {'labels': {'app': name}}, 'spec': {'automountServiceAccountToken': False,
                        'securityContext': security, 'containers': [container]}}}})
        hpa = {'apiVersion': 'autoscaling/v2', 'kind': 'HorizontalPodAutoscaler',
            'metadata': {'name': 'fullnet-api-resource-fixture', 'namespace': 'fullnet-production'},
            'spec': {'minReplicas': 2, 'maxReplicas': 4,
                'scaleTargetRef': {'apiVersion': 'apps/v1', 'kind': 'Deployment', 'name': 'resource-client'},
                'metrics': [{'type': 'Resource', 'resource': {'name': 'cpu',
                    'target': {'type': 'Utilization', 'averageUtilization': 50}}}]}}
        pdb = {'apiVersion': 'policy/v1', 'kind': 'PodDisruptionBudget',
            'metadata': {'name': 'fullnet-resource-fixture', 'namespace': 'fullnet-production'},
            'spec': {'minAvailable': 1, 'selector': {'matchLabels': {'app': 'resource-client'}}}}
        self.apply(hpa)
        self.apply(pdb)
        for name, namespace in (('prometheus', 'observability'), ('kube-state-metrics', 'observability'),
                ('resource-client', 'fullnet-production'), ('metrics-observer', 'observability')):
            self.kubectl('-n', namespace, 'rollout', 'status', 'deployment/' + name, '--timeout=180s', seconds=190)
        identity = 'system:serviceaccount:observability:fullnet-metrics-reader'
        for verb, resource, namespace, allowed in (
                ('list', 'horizontalpodautoscalers.autoscaling', 'fullnet-production', True),
                ('list', 'poddisruptionbudgets.policy', 'fullnet-production', True),
                ('list', 'horizontalpodautoscalers.autoscaling', 'default', False),
                ('list', 'poddisruptionbudgets.policy', 'default', False),
                ('patch', 'horizontalpodautoscalers.autoscaling', 'fullnet-production', False),
                ('patch', 'poddisruptionbudgets.policy', 'fullnet-production', False),
                ('list', 'secrets', 'fullnet-production', False)):
            result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, 'auth', 'can-i', verb,
                resource, '-n', namespace, '--as=' + identity], env=self.environment,
                capture_output=True, text=True, timeout=20)
            self.assertEqual(result.returncode, 0 if allowed else 1)
            self.assertEqual(result.stdout.strip(), 'yes' if allowed else 'no')
        hpa_max = ('kube_horizontalpodautoscaler_spec_max_replicas{cluster="test-cluster",'
            'namespace="fullnet-production",horizontalpodautoscaler="fullnet-api-resource-fixture"}')
        hpa_current = hpa_max.replace('spec_max_replicas', 'status_current_replicas')
        pdb_allowed = ('kube_poddisruptionbudget_status_pod_disruptions_allowed{cluster="test-cluster",'
            'namespace="fullnet-production",poddisruptionbudget="fullnet-resource-fixture"}')
        self.wait(lambda: any(float(v['value'][1]) == 4 for v in self.query(hpa_max)))
        self.wait(lambda: len(self.query(hpa_current)) == 1)
        self.wait(lambda: any(float(v['value'][1]) == 1 for v in self.query(pdb_allowed)))
        hpa['spec']['maxReplicas'] = 6
        pdb['spec']['minAvailable'] = 2
        self.apply(hpa)
        self.apply(pdb)
        self.wait(lambda: any(float(v['value'][1]) == 6 for v in self.query(hpa_max)))
        self.wait(lambda: any(float(v['value'][1]) == 0 for v in self.query(pdb_allowed)))
        # 删除本轮 HPA/PDB，确认 exporter 的陈旧系列被标记移除，不能保留旧状态假报正常。
        self.kubectl('-n', 'fullnet-production', 'delete', 'hpa/fullnet-api-resource-fixture',
            'pdb/fullnet-resource-fixture', '--wait=true')
        self.wait(lambda: not self.query(hpa_max) and not self.query(hpa_current) and not self.query(pdb_allowed))
        print('workload_metrics_watch: hpa_max=4->6 current_series=present pdb_allowed=1->0 deleted_series=absent', flush=True)
        print('workload_metrics_rbac: namespace_reads=allowed foreign_reads=denied writes=denied secrets=denied', flush=True)

    def test_original_manifests_watch_deployments_and_preserve_both_pvc_states_after_pod_replacement(self):
        platform = bundle()
        definitions = deployments(platform)
        self.kubectl('create', 'namespace', 'fullnet-production')
        self.apply(next(item for item in platform['items'] if item['kind'] == 'Namespace'))
        for name in ('prometheus-data', 'alertmanager-data'):
            self.apply({'apiVersion': 'v1', 'kind': 'PersistentVolumeClaim',
                'metadata': {'name': name, 'namespace': 'observability'},
                'spec': {'accessModes': ['ReadWriteOnce'], 'storageClassName': 'standard',
                    'resources': {'requests': {'storage': '1Gi'}}}})
        # 两个小型动态卷只用于验证权限和同节点 Pod 重建，不修改正式清单中的存储配置。
        self.apply(platform)
        image = 'python:3.12-slim-bookworm'
        container = {'name': 'client', 'image': image, 'command': ['python3', '-c', 'import time; time.sleep(3600)'],
            'resources': {'requests': {'cpu': '50m', 'memory': '32Mi'}, 'limits': {'cpu': '200m', 'memory': '128Mi'}},
            'securityContext': {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True,
                'capabilities': {'drop': ['ALL']}}}
        client = {'apiVersion': 'apps/v1', 'kind': 'Deployment',
            'metadata': {'name': 'metrics-client', 'namespace': 'fullnet-production'},
            'spec': {'replicas': 2, 'selector': {'matchLabels': {'app': 'metrics-client'}},
                'template': {'metadata': {'labels': {'app': 'metrics-client'}}, 'spec': {
                    'automountServiceAccountToken': False,
                    'securityContext': {'runAsNonRoot': True, 'runAsUser': 65534,
                        'seccompProfile': {'type': 'RuntimeDefault'}}, 'containers': [container]}}}}
        self.apply(client)
        observer = json.loads(json.dumps(client))
        observer['metadata'] = {'name': 'metrics-observer', 'namespace': 'observability'}
        observer['spec']['replicas'] = 1
        observer['spec']['selector']['matchLabels'] = {'app': 'metrics-observer'}
        observer['spec']['template']['metadata']['labels'] = {'app': 'metrics-observer'}
        self.apply(observer)
        print('metrics_kubernetes_stage: waiting for unmodified platform deployments', flush=True)
        for name in definitions:
            try:
                self.kubectl('-n', 'observability', 'rollout', 'status', 'deployment/' + name, '--timeout=180s', seconds=190)
            except RuntimeError:
                print(self.kubectl('-n', 'observability', 'get', 'pods', '-o', 'wide'), flush=True)
                print(self.kubectl('-n', 'observability', 'get', 'events', '--field-selector', 'type=Warning'), flush=True)
                raise
        self.kubectl('-n', 'fullnet-production', 'rollout', 'status', 'deployment/metrics-client', '--timeout=120s')
        self.kubectl('-n', 'observability', 'rollout', 'status', 'deployment/metrics-observer', '--timeout=120s')
        claims = json.loads(self.kubectl('-n', 'observability', 'get', 'pvc', '-o', 'json'))['items']
        claim_ids = {item['metadata']['name']: item['metadata']['uid'] for item in claims}
        self.assertEqual(len(claim_ids), 2)
        self.assertTrue(all(item['status']['phase'] == 'Bound' for item in claims))
        pods = json.loads(self.kubectl('-n', 'observability', 'get', 'pods',
            '-l', 'app.kubernetes.io/part-of=fullnet-observability', '-o', 'json'))['items']
        self.assertEqual(len(pods), 4)
        for pod in pods:
            name = pod['metadata']['labels']['app.kubernetes.io/name']
            self.assertEqual(pod['spec']['automountServiceAccountToken'], name == 'kube-state-metrics')
            self.assertTrue(pod['spec']['containers'][0]['securityContext']['readOnlyRootFilesystem'])
            self.assertTrue(pod['spec']['securityContext']['runAsNonRoot'])
            self.assertEqual(len(pod['status']['containerStatuses']), 1)
            self.assertTrue(pod['status']['containerStatuses'][0]['ready'])
            print('metrics_kubernetes_image: ' + name + '=' + pod['status']['containerStatuses'][0]['imageID'], flush=True)
        identity = 'system:serviceaccount:observability:fullnet-metrics-reader'
        for resource, namespace, allowed in (('deployments.apps', 'fullnet-production', True),
                ('secrets', 'fullnet-production', False), ('deployments.apps', 'default', False)):
            result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, 'auth', 'can-i',
                'list', resource, '-n', namespace, '--as=' + identity], env=self.environment,
                capture_output=True, text=True, timeout=20)
            self.assertEqual(result.returncode, 0 if allowed else 1)
            self.assertEqual(result.stdout.strip(), 'yes' if allowed else 'no')
        self.wait(lambda: any(float(v['value'][1]) == 2 for v in self.query(
            'kube_deployment_status_replicas_available{namespace="fullnet-production",deployment="metrics-client"}')))
        self.kubectl('-n', 'fullnet-production', 'scale', 'deployment/metrics-client', '--replicas=1')
        self.wait(lambda: any(float(v['value'][1]) == 1 for v in self.query(
            'kube_deployment_status_replicas_available{namespace="fullnet-production",deployment="metrics-client"}')))
        print('metrics_kubernetes_watch: replicas=2->1 secret_list=denied foreign_namespace=denied', flush=True)
        instance = uuid.uuid4().hex
        emitted = time.time()
        self.http('http://telemetry-collector.observability.svc:4318/v1/metrics', {'resourceMetrics': [{
            'resource': {'attributes': [{'key': 'service.name', 'value': {'stringValue': 'fullnet-kind-fixture'}},
                {'key': 'service.instance.id', 'value': {'stringValue': instance}}]},
            'scopeMetrics': [{'scope': {'name': 'isolated-kubernetes-fixture'}, 'metrics': [{
                'name': 'fullnet_kubernetes_pipeline_probe', 'gauge': {'dataPoints': [{
                    'timeUnixNano': str(int(emitted * 1_000_000_000)), 'asDouble': 7}]}}]}]}]})
        expression = 'fullnet_kubernetes_pipeline_probe{job="fullnet-kind-fixture",instance="' + instance + '"}'
        self.wait(lambda: any(float(v['value'][1]) == 7 for v in self.query(expression)), 45)
        sample_time = self.query('timestamp(' + expression + ')')
        self.assertEqual(len(sample_time), 1)
        self.assertGreaterEqual(float(sample_time[0]['value'][1]), emitted - 1)
        self.assertLess(abs(time.time() - float(sample_time[0]['value'][1])), 45)
        now = datetime.datetime.now(datetime.timezone.utc)
        silence = self.http('http://alertmanager.observability.svc:9093/api/v2/silences', {
            'matchers': [{'name': 'alertname', 'value': 'FullNetIsolatedPersistenceFixture', 'isRegex': False}],
            'startsAt': (now - datetime.timedelta(seconds=1)).isoformat(),
            'endsAt': (now + datetime.timedelta(hours=1)).isoformat(), 'createdBy': 'isolated-fixture',
            'comment': 'owned persistence fixture'})['silenceID']
        self.replace_pod('prometheus')
        self.assertTrue(any(float(v['value'][1]) == 7 for v in self.query(expression)))
        self.replace_pod('alertmanager')
        self.assertEqual(self.http('http://alertmanager.observability.svc:9093/api/v2/silence/' + silence)
            ['status']['state'], 'active')
        after = json.loads(self.kubectl('-n', 'observability', 'get', 'pvc', '-o', 'json'))['items']
        self.assertEqual({item['metadata']['name']: item['metadata']['uid'] for item in after}, claim_ids)
        print('metrics_kubernetes_persistence: prometheus_sample=retained alertmanager_silence=retained pvc_uids=unchanged', flush=True)


if __name__ == '__main__':
    unittest.main()
