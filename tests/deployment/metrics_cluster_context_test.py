"""平台 cluster 必须覆盖不可信输入，且不能破坏身份过滤和原有资源标识。"""
import unittest
import json
import os
from unittest.mock import patch

from metrics_platform_test import bundle, configuration


class MetricsClusterContextTests(unittest.TestCase):
    def test_both_points_and_resources_upsert_authoritative_platform_cluster(self):
        config = configuration(bundle())['collector.json']
        for name, field in (('attributes/drop_high_cardinality', 'actions'), ('resource/drop_high_cardinality', 'attributes')):
            actions = config['processors'][name][field]
            cluster = [action for action in actions if action['key'] == 'cluster']
            self.assertEqual(cluster, [{'key': 'cluster', 'action': 'upsert', 'value': 'test-cluster'}])
            self.assertTrue({'tenant_id', 'user_id', 'DiagnosticGroup'}.issubset(
                {action['key'] for action in actions if action['action'] == 'delete'}))
        self.assertEqual(config['service']['pipelines']['metrics']['processors'],
            ['memory_limiter', 'resource/drop_high_cardinality', 'attributes/drop_high_cardinality', 'batch'])

    def test_another_platform_uses_its_own_configured_value(self):
        config = configuration(bundle('--set', 'cluster=secondary-platform'))['collector.json']
        for name, field in (('attributes/drop_high_cardinality', 'actions'), ('resource/drop_high_cardinality', 'attributes')):
            self.assertEqual(next(action for action in config['processors'][name][field] if action['key'] == 'cluster')['value'],
                'secondary-platform')

    def test_runtime_fixture_rejects_remote_override_before_creating_resources(self):
        import metrics_platform_runtime_test as runtime
        fixture = runtime.MetricsPlatformRuntimeFixture()
        context = json.dumps([{'Endpoints': {'docker': {'Host': 'unix:///var/run/docker.sock'}}}])
        with patch.dict(os.environ, {'FULLNET_METRICS_REAL': 'isolated', 'DOCKER_HOST': 'tcp://192.0.2.1:2375'}), \
                patch.object(runtime, 'command', return_value=context) as call:
            try:
                with self.assertRaises(AssertionError):
                    fixture.setUp()
                call.assert_called_once_with(['docker', 'context', 'inspect'])
            finally:
                fixture.doCleanups()


if __name__ == '__main__':
    unittest.main()
