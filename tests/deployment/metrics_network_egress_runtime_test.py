"""固定 Calico 的独立 Kind 验证精确网络许可；端口夹具不冒充真实业务或 OTLP 数据流。"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
import unittest
import uuid

from cluster_network_test import NODE_IMAGE, real_command
from metrics_network_egress_test import policy, render
from metrics_platform_test import bundle

CALICO_SHA256 = '2de8f47595fb9c41b3f47d7b767a1f8e72ecf84057af834738ff12689a234da5'
CALICO_IMAGES = {
    'quay.io/calico/calico:v3.33.0': 'sha256:4ebc20730127bc9e8bf1db5f5a2d8b58cf37fe0e891b3c7b5471992221d213e5',
    'quay.io/calico/node:v3.33.0': 'sha256:565d8db3200e443955557f8f28ff44ab8a4a456754bc74a19c4f2f3444af9b1c',
    'quay.io/calico/third-party-cni-plugins:v3.33.0': 'sha256:0f32f48279484c0d6f0e0d0e767b2d0e1a6c6ca34cb3421a61b989d992f3df41'}
PYTHON = 'python:3.12-slim-bookworm'
SERVER = '''import socket,threading,time
def serve(port):
 s=socket.socket();s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(('0.0.0.0',port));s.listen()
 while True:
  connection,_=s.accept()
  with connection:connection.sendall(('fullnet-metrics-network:'+str(port)).encode())
for port in (4317,4318,9090):threading.Thread(target=serve,args=(port,),daemon=True).start()
time.sleep(3600)
'''
PROBE = '''import json,socket,sys
result={'connected':False}
try:
 with socket.create_connection((sys.argv[1],int(sys.argv[2])),timeout=1) as channel:
  result['connected']=True
  result['payload']=channel.recv(128).decode()
except OSError:pass
print(json.dumps(result))
'''


class MetricsNetworkEgressRuntimeTests(unittest.TestCase):
    def command(self, arguments, seconds=180):
        return real_command(arguments, self.environment, seconds).strip()

    def kubectl(self, *arguments, seconds=180):
        return self.command(['kubectl', '--context', 'kind-' + self.name, *arguments], seconds)

    def apply(self, value):
        content = value if isinstance(value, str) else json.dumps(value)
        result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, 'apply', '-f', '-'],
            input=content, env=self.environment, capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, (result.stdout + result.stderr)[-2000:])

    def setUp(self):
        self.assertEqual(os.environ.get('FULLNET_METRICS_NETWORK_REAL'), 'isolated')
        context = json.loads(real_command(['docker', 'context', 'inspect']))[0]['Endpoints']['docker']['Host']
        self.assertTrue(context.startswith('unix://'))
        self.assertIn(os.environ.get('DOCKER_HOST', context), ('', context))
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-metrics-network-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.name = 'fullnet-metrics-net-' + uuid.uuid4().hex[:10]
        self.environment = {**os.environ, 'KUBECONFIG': str(self.root / 'kubeconfig')}
        self.assertNotIn(self.name, self.command(['kind', 'get', 'clusters']).split())
        self.addCleanup(self.cleanup_cluster)
        config = self.root / 'kind.json'
        config.write_text(json.dumps({'kind': 'Cluster', 'apiVersion': 'kind.x-k8s.io/v1alpha4',
            'nodes': [{'role': 'control-plane'}, {'role': 'worker'}],
            'networking': {'disableDefaultCNI': True, 'podSubnet': '192.168.0.0/16'}}))
        self.command(['kind', 'create', 'cluster', '--name', self.name, '--image', NODE_IMAGE,
            '--config', config, '--kubeconfig', self.environment['KUBECONFIG']], 240)
        manifest = Path(os.environ['FULLNET_CALICO_MANIFEST']).read_bytes()
        self.assertEqual(hashlib.sha256(manifest).hexdigest(), CALICO_SHA256)
        text = manifest.decode()
        for image, digest in CALICO_IMAGES.items():
            self.assertIn('image: ' + image, text)
            text = text.replace('image: ' + image, 'image: ' + image + '@' + digest)
        self.assertEqual(set(re.findall(r'^\s*image:\s*(\S+)', text, re.M)),
            {image + '@' + digest for image, digest in CALICO_IMAGES.items()})
        # 大型第三方 CRD 不存入仓库，也不以 client-side apply 注解复制到每个对象。
        result = subprocess.run(['kubectl', '--context', 'kind-' + self.name, 'create', '-f', '-'],
            input=text, env=self.environment, capture_output=True, text=True, timeout=90)
        self.assertEqual(result.returncode, 0, (result.stdout + result.stderr)[-3000:])
        self.kubectl('-n', 'kube-system', 'rollout', 'status', 'daemonset/calico-node', '--timeout=240s', seconds=270)
        self.kubectl('-n', 'kube-system', 'rollout', 'status', 'deployment/calico-kube-controllers', '--timeout=180s', seconds=210)
        self.kubectl('wait', '--for=condition=Ready', 'nodes', '--all', '--timeout=180s', seconds=210)
        self.command(['docker', 'pull', PYTHON], 180)
        self.command(['kind', 'load', 'docker-image', PYTHON, '--name', self.name], 180)
        print('metrics_network_stage: calico=3.33.0 pinned_manifest_and_images=true two_nodes_ready=true', flush=True)

    def cleanup_cluster(self):
        nodes = self.command(['docker', 'ps', '-aq', '--filter', 'label=io.x-k8s.kind.cluster=' + self.name]).split()
        for node in nodes:
            self.assertEqual(json.loads(self.command(['docker', 'inspect', node]))[0]['Config']['Labels'].get('io.x-k8s.kind.cluster'), self.name)
        if nodes:
            self.command(['kind', 'delete', 'cluster', '--name', self.name], 120)
        self.assertEqual(self.command(['docker', 'ps', '-aq', '--filter', 'label=io.x-k8s.kind.cluster=' + self.name]), '')
        print('metrics_network_cleanup: own_cluster_containers=0', flush=True)

    def pod(self, name, namespace, labels, *, server=False):
        self.apply({'apiVersion': 'v1', 'kind': 'Pod', 'metadata': {'name': name, 'namespace': namespace, 'labels': labels},
            'spec': {'nodeName': self.name + ('-control-plane' if server else '-worker'), 'automountServiceAccountToken': False,
                'securityContext': {'runAsNonRoot': True, 'runAsUser': 65534, 'runAsGroup': 65534, 'seccompProfile': {'type': 'RuntimeDefault'}},
                'containers': [{'name': 'fixture', 'image': PYTHON, 'imagePullPolicy': 'IfNotPresent',
                    'command': ['python3', '-c', SERVER if server else 'import time; time.sleep(3600)'],
                    'securityContext': {'allowPrivilegeEscalation': False, 'readOnlyRootFilesystem': True, 'capabilities': {'drop': ['ALL']}},
                    'resources': {'requests': {'cpu': '10m', 'memory': '16Mi'}, 'limits': {'cpu': '200m', 'memory': '64Mi'}},
                    **({'readinessProbe': {'tcpSocket': {'port': 4318}, 'periodSeconds': 1}} if server else {})}]}})

    def probe(self, name, destination, port, namespace='fullnet-production'):
        return json.loads(self.kubectl('-n', namespace, 'exec', name, '--', 'python3', '-c', PROBE, destination, str(port), seconds=15))

    def check(self, name, destination, port, allowed, namespace='fullnet-production'):
        deadline = time.monotonic() + 45
        last = None
        while time.monotonic() < deadline:
            last = self.probe(name, destination, port, namespace)
            if last['connected'] == allowed and (not allowed or last.get('payload') == 'fullnet-metrics-network:' + str(port)):
                return
            time.sleep(.5)
        self.fail(f'精确网络条件未满足：{name}:{port} allowed={allowed} actual={last}')

    def test_real_cni_enforces_opt_in_peer_roles_ports_and_revocation(self):
        for namespace in ('fullnet-production', 'observability', 'metrics-other'):
            self.apply({'apiVersion': 'v1', 'kind': 'Namespace', 'metadata': {'name': namespace,
                'labels': {'pod-security.kubernetes.io/enforce': 'restricted'}}})
        collector = {'app.kubernetes.io/part-of': 'fullnet-observability', 'app.kubernetes.io/name': 'telemetry-collector'}
        self.pod('collector', 'observability', collector, server=True)
        self.pod('wrong-pod', 'observability', {**collector, 'app.kubernetes.io/name': 'unrelated'}, server=True)
        self.pod('foreign-collector', 'metrics-other', collector, server=True)
        for name, role, release in (('api', 'api', 'metrics-api'), ('worker', 'worker', 'metrics-worker'),
                ('migrator', 'migrator', 'metrics-api'), ('other-release', 'api', 'metrics-other-release')):
            self.pod(name, 'fullnet-production', {'app.kubernetes.io/name': 'fullnet', 'app.kubernetes.io/instance': release,
                'app.kubernetes.io/component': role})
        self.pod('observer', 'observability', {'app': 'network-observer'})
        self.pod('foreign-source', 'metrics-other', {'app': 'network-source'})
        for namespace in ('fullnet-production', 'observability', 'metrics-other'):
            self.kubectl('-n', namespace, 'wait', '--for=condition=Ready', 'pod', '--all', '--timeout=120s')
        def servers():
            return {name: json.loads(self.kubectl('-n', namespace, 'get', 'pod', name, '-o', 'json'))
                for name, namespace in (('collector', 'observability'), ('wrong-pod', 'observability'), ('foreign-collector', 'metrics-other'))}
        before = servers()
        self.assertEqual({pod['spec']['nodeName'] for pod in before.values()}, {self.name + '-control-plane'})
        self.assertEqual(json.loads(self.kubectl('-n', 'fullnet-production', 'get', 'pod', 'api', '-o', 'json'))['spec']['nodeName'], self.name + '-worker')
        addresses = {name: pod['status']['podIP'] for name, pod in before.items()}
        for destination in addresses.values():
            for port in (4317, 4318, 9090):
                self.check('observer', destination, port, True, 'observability')
        for role, release in (('api', None), ('worker', None), ('api', 'metrics-other-release')):
            baseline = render(role, template='networkpolicy.yaml', release=release)
            self.assertEqual(baseline.returncode, 0, baseline.stderr)
            self.apply(baseline.stdout)
        self.apply({'apiVersion': 'v1', 'kind': 'Service', 'metadata': {'name': 'telemetry-collector', 'namespace': 'observability'},
            'spec': {'selector': collector, 'ports': [{'name': 'grpc', 'port': 4317}, {'name': 'http', 'port': 4318}]}})
        cluster_ip = json.loads(self.kubectl('-n', 'observability', 'get', 'service', 'telemetry-collector', '-o', 'json'))['spec']['clusterIP']
        fqdn = 'telemetry-collector.observability.svc.cluster.local'
        self.assertEqual(self.kubectl('-n', 'fullnet-production', 'exec', 'api', '--', 'python3', '-c',
            'import socket,sys;print(socket.gethostbyname(sys.argv[1]))', fqdn), cluster_ip)
        for source in ('api', 'worker'):
            self.check(source, cluster_ip, 4318, False)
        permissions = [policy('api'), policy('worker')]
        for permission in permissions:
            self.apply(permission)
        self.check('api', fqdn, 4318, True)
        self.check('worker', cluster_ip, 4317, True)
        for source, destination, port in (('api', addresses['wrong-pod'], 4318), ('api', addresses['foreign-collector'], 4318),
                ('api', addresses['collector'], 9090), ('migrator', cluster_ip, 4318), ('other-release', cluster_ip, 4318)):
            self.check(source, destination, port, False)
        platform = bundle()
        self.apply(next(item for item in platform['items'] if item['kind'] == 'NetworkPolicy'))
        self.check('api', fqdn, 4318, True)
        self.check('foreign-source', addresses['collector'], 4318, False, 'metrics-other')
        for permission in permissions:
            self.kubectl('-n', 'fullnet-production', 'delete', 'networkpolicy', permission['metadata']['name'], '--wait=true')
        for source in ('api', 'worker'):
            self.check(source, cluster_ip, 4318, False)
        self.check('observer', addresses['collector'], 9090, True, 'observability')
        after = servers()
        self.assertEqual({name: pod['metadata']['uid'] for name, pod in before.items()},
            {name: pod['metadata']['uid'] for name, pod in after.items()})
        self.assertTrue(all(pod['status']['phase'] == 'Running' for pod in after.values()))
        print('metrics_network: real_cni=calico-3.33.0 opt_in=deny->allow->deny service_dns_and_clusterip=verified roles_release_namespace_pod_ports=restricted server_uids=stable', flush=True)
