"""独立 Kind API 验证精确代理配置与条件更新；不声称 Kind CNI 实施 NetworkPolicy。"""
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
import uuid

from cluster_network_test import NODE_IMAGE, real_command
from gateway_trust_test import SCRIPT, TRUST


class GatewayTrustRuntimeTests(unittest.TestCase):
    def test_real_api_exact_update_idempotency_and_concurrent_write_rejection(self):
        for command in ('kind', 'kubectl', 'docker'):
            self.assertIsNotNone(shutil.which(command), command + ' 是必需条件，不能跳过。')
        self.assertTrue(sys.platform.startswith('linux'))
        docker_context = json.loads(real_command(['docker', 'context', 'inspect']))[0]
        self.assertTrue(docker_context['Endpoints']['docker']['Host'].startswith('unix://'))
        name = 'fullnet-trust-' + uuid.uuid4().hex[:12]
        self.assertNotIn(name, real_command(['kind', 'get', 'clusters']).split())
        with tempfile.TemporaryDirectory(prefix='fullnet-gateway-trust-') as directory:
            root = Path(directory)
            environment = {**os.environ, 'KUBECONFIG': str(root / 'kubeconfig')}
            context = 'kind-' + name
            prefix = ['kubectl', '--context', context, '--request-timeout=15s']
            def kubectl(*arguments):
                return real_command([*prefix, *arguments], environment, seconds=60)
            try:
                real_command(['kind', 'create', 'cluster', '--name', name, '--image', NODE_IMAGE,
                    '--kubeconfig', environment['KUBECONFIG'], '--wait', '180s'], environment, seconds=240)
                nodes = json.loads(kubectl('get', 'nodes', '-o', 'json'))['items']
                self.assertEqual(len(nodes), 1)
                node = nodes[0]
                primary = next(value['address'] for value in node['status']['addresses'] if value['type'] == 'InternalIP')
                kubectl('create', 'namespace', TRUST.NAMESPACE)
                kubectl('-n', TRUST.NAMESPACE, 'create', 'configmap', TRUST.CONFIGURATION,
                    '--from-literal=Other=preserved', '--from-literal=TrustedProxy__KnownProxies__9=10.244.99.99',
                    '--from-literal=TrustedProxy__KnownNetworks__0=10.244.0.0/16')
                command = [sys.executable, '-O', str(SCRIPT), primary, '--kubectl', shutil.which('kubectl'), '--context', context]
                first = json.loads(real_command(command, environment, seconds=120))
                self.assertEqual(first['status'], 'configured')
                self.assertFalse(first['runtimeActivationVerified'])
                self.assertTrue(first['apiRolloutRequired'])
                def get(kind, resource):
                    return json.loads(kubectl('-n', TRUST.NAMESPACE, 'get', kind, resource, '-o', 'json'))
                configuration = get('configmap', TRUST.CONFIGURATION)
                policy = get('networkpolicy', TRUST.POLICY)
                self.assertEqual(configuration['data'], {'Other': 'preserved', 'TrustedProxy__Enabled': 'true',
                    'TrustedProxy__ForwardLimit': '1', 'TrustedProxy__KnownProxies__0': primary,
                    'TrustedProxy__KnownProxies__1': first['overlay']})
                self.assertEqual(policy['spec'], TRUST.desired_policy(primary, first['overlay']))
                second = json.loads(real_command(command, environment, seconds=120))
                self.assertEqual(second['status'], 'configured')
                self.assertEqual(get('configmap', TRUST.CONFIGURATION)['metadata']['resourceVersion'], configuration['metadata']['resourceVersion'])
                self.assertEqual(get('networkpolicy', TRUST.POLICY)['metadata']['resourceVersion'], policy['metadata']['resourceVersion'])
                kubectl('-n', TRUST.NAMESPACE, 'patch', 'configmap', TRUST.CONFIGURATION, '--type=merge',
                        '-p', json.dumps({'data': {'TrustedProxy__KnownProxies__9': '10.244.99.99'}}))
                state = {'networkPolicy': 'not_attempted', 'runtimeConfig': 'not_attempted'}
                client = TRUST.Client(prefix)
                original_command = client.command
                injected = []
                def concurrent(arguments, payload=None):
                    if 'patch' in arguments and 'ConfigMap' in arguments and not injected:
                        injected.append(True)
                        kubectl('-n', TRUST.NAMESPACE, 'patch', 'configmap', TRUST.CONFIGURATION, '--type=merge',
                                '-p', json.dumps({'data': {'Concurrent': 'preserved'}}))
                    return original_command(arguments, payload)
                client.command = concurrent
                with patch.dict(os.environ, {'KUBECONFIG': environment['KUBECONFIG']}):
                    with self.assertRaises(TRUST.TrustFailure) as failure:
                        TRUST.configure(client, primary, state)
                self.assertEqual(str(failure.exception), 'resource_write_not_verified')
                self.assertEqual(state, {'networkPolicy': 'verified', 'runtimeConfig': 'not_verified'})
                conflicted = get('configmap', TRUST.CONFIGURATION)
                self.assertEqual(conflicted['data']['Concurrent'], 'preserved')
                self.assertIn('TrustedProxy__KnownProxies__9', conflicted['data'])
                final = json.loads(real_command(command, environment, seconds=120))
                self.assertEqual(final['status'], 'configured')
                restored = get('configmap', TRUST.CONFIGURATION)
                self.assertEqual(restored['data']['Concurrent'], 'preserved')
                self.assertNotIn('TrustedProxy__KnownProxies__9', restored['data'])
                print('gateway_trust_real_receipt: ' + json.dumps({
                    'nodeUid': node['metadata']['uid'], 'configUid': configuration['metadata']['uid'],
                    'policyUid': policy['metadata']['uid'], 'exactProxyCount': 2, 'cidrPrefix': 32,
                    'idempotentResourceVersions': True, 'conflictRejected': True, 'concurrentValuePreserved': True,
                    'recoveryRetryVerified': True, 'runtimeActivationVerified': False}), flush=True)
            finally:
                real_command(['kind', 'delete', 'cluster', '--name', name], environment, seconds=120)
                self.assertNotIn(name, real_command(['kind', 'get', 'clusters']).split())
                self.assertEqual(real_command(['docker', 'ps', '-aq', '--filter', 'label=io.x-k8s.kind.cluster=' + name]).strip(), '')
                print('gateway_trust_cleanup: own_cluster_containers=0', flush=True)


if __name__ == '__main__':
    unittest.main()
