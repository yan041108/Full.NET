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
