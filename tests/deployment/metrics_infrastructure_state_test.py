"""节点状态与 DaemonSet 观察按明确开关独立授权，不扩大默认应用账号。"""
import unittest

from metrics_platform_test import bundle, configuration, deployments, render


class InfrastructureStateTests(unittest.TestCase):
    def test_default_keeps_cluster_permissions_and_optional_readers_disabled(self):
        value = bundle()
        self.assertFalse(any(item['kind'].startswith('ClusterRole') for item in value['items']))
        self.assertEqual(set(deployments(value)), {'prometheus', 'alertmanager', 'telemetry-collector', 'kube-state-metrics'})
        jobs = configuration(value)['prometheus.json']['scrape_configs']
        self.assertFalse(any(job['job_name'] in ('fullnet-kube-state-nodes', 'fullnet-kube-state-daemonsets') for job in jobs))

    def test_explicit_readers_have_separate_accounts_and_exact_scopes(self):
        value = bundle('--set', 'nodeState.enabled=true,daemonSetNamespace=fullnet-logging')
        resources = value['items']
        role = next(item for item in resources if item['kind'] == 'ClusterRole')
        self.assertEqual(role['metadata']['name'], 'observability-fullnet-metrics-nodes')
        self.assertEqual(role['rules'], [{'apiGroups': [''], 'resources': ['nodes'], 'verbs': ['get', 'list', 'watch']}])
        binding = next(item for item in resources if item['kind'] == 'ClusterRoleBinding')
        self.assertEqual(binding['subjects'], [{'kind': 'ServiceAccount', 'name': 'fullnet-metrics-node-reader', 'namespace': 'observability'}])
        self.assertEqual(binding['roleRef']['name'], role['metadata']['name'])
        daemon_role = next(item for item in resources if item['kind'] == 'Role' and item['metadata']['namespace'] == 'fullnet-logging')
        self.assertEqual(daemon_role['rules'], [{'apiGroups': ['apps'], 'resources': ['daemonsets'], 'verbs': ['get', 'list', 'watch']}])
        actual = deployments(value)
        self.assertEqual(len(actual), 6)
        expected = {'kube-state-nodes': 'fullnet-metrics-node-reader', 'kube-state-daemonsets': 'fullnet-metrics-daemonset-reader'}
        for name, account in expected.items():
            pod = actual[name]['spec']['template']['spec']
            self.assertEqual(pod['serviceAccountName'], account)
            self.assertTrue(pod['automountServiceAccountToken'])
            container = pod['containers'][0]
            self.assertEqual(container['readinessProbe']['httpGet'], {'path': '/metrics', 'port': 8080})
            self.assertEqual(container['image'], actual['kube-state-metrics']['spec']['template']['spec']['containers'][0]['image'])
            self.assertTrue(container['securityContext']['readOnlyRootFilesystem'])
            self.assertTrue(pod['securityContext']['runAsNonRoot'])
            self.assertEqual(pod['volumes'], [])
        self.assertIn('--resources=nodes', actual['kube-state-nodes']['spec']['template']['spec']['containers'][0]['args'])
        self.assertIn('--namespaces=fullnet-logging', actual['kube-state-daemonsets']['spec']['template']['spec']['containers'][0]['args'])
        jobs = {job['job_name']: job for job in configuration(value)['prometheus.json']['scrape_configs']}
        for suffix in ('nodes', 'daemonsets'):
            self.assertEqual(jobs['fullnet-kube-state-' + suffix]['static_configs'],
                [{'targets': ['kube-state-' + suffix + '.observability.svc:8080'], 'labels': {'cluster': 'test-cluster'}}])

    def test_each_optional_scope_is_independent_and_invalid_settings_are_rejected(self):
        nodes = bundle('--set', 'nodeState.enabled=true')
        self.assertNotIn('kube-state-daemonsets', deployments(nodes))
        daemonsets = bundle('--set', 'daemonSetNamespace=fullnet-logging')
        self.assertNotIn('kube-state-nodes', deployments(daemonsets))
        self.assertFalse(any(item['kind'].startswith('ClusterRole') for item in daemonsets['items']))
        for arguments in (('--set-string', 'nodeState.enabled=false'), ('--set', 'daemonSetNamespace=false'),
                ('--set-string', 'daemonSetNamespace=../other'), ('--set', 'nodeState.allowProxy=true')):
            with self.subTest(arguments=arguments):
                self.assertNotEqual(render(*arguments).returncode, 0)


if __name__ == '__main__':
    unittest.main()
