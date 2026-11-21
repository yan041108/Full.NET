"""用真实 Collector/Remote Write 核对平台归属、原实例身份和资源过滤。"""
import json
import time
import unittest
import urllib.parse

import metrics_platform_runtime_test as runtime


class MetricsClusterContextRuntimeTests(runtime.MetricsPlatformRuntimeFixture):
    components = ('prometheus', 'telemetry-collector')

    def test_missing_and_spoofed_cluster_are_normalized_on_metrics_and_target_info(self):
        self.receiver = self.run_container('probe', 'python:3.12-slim-bookworm',
            ['python3', '-c', 'import time; time.sleep(3600)'])
        prom = self.definitions['prometheus']['spec']['template']['spec']['containers'][0]
        self.run_container('prometheus', prom['image'], prom['args'], mounts=[(self.config_directory, '/etc/fullnet')],
            tmpfs=['/prometheus:uid=65534,gid=65534,mode=0700,size=64m'], alias='prometheus.observability.svc')
        otel = self.definitions['telemetry-collector']['spec']['template']['spec']['containers'][0]
        self.run_container('collector', otel['image'], otel['args'], uid='10001:10001',
            mounts=[(self.config_directory, '/etc/fullnet')],
            tmpfs=['/var/otel-collector/storage:uid=10001,gid=10001,mode=0700,size=64m'],
            alias='telemetry-collector.observability.svc')
        stamp = time.time_ns()
        resources = []
        for suffix, value in (('missing', 7), ('spoofed', 9)):
            private = [{'key': key, 'value': {'stringValue': 'private-context-sentinel'}}
                for key in ('tenant_id', 'user_id', 'DiagnosticGroup')]
            attributes = [*private, {'key': 'route', 'value': {'stringValue': 'bounded-fixture'}}]
            resource = [*private, {'key': 'service.name', 'value': {'stringValue': 'fullnet-cluster-fixture'}},
                {'key': 'service.instance.id', 'value': {'stringValue': self.network + '-' + suffix}}]
            if suffix == 'spoofed':
                resource.append({'key': 'cluster', 'value': {'stringValue': 'forged-resource-cluster'}})
                attributes.append({'key': 'cluster', 'value': {'intValue': '999'}})
            metrics = [{'name': 'fullnet_platform_context_' + suffix, 'gauge': {'dataPoints': [{
                'asDouble': value, 'timeUnixNano': str(stamp), 'attributes': attributes}]}}]
            if suffix == 'spoofed':
                metrics.append({'name': 'fullnet_platform_context_events', 'sum': {
                    'aggregationTemporality': 2, 'isMonotonic': True, 'dataPoints': [{
                        'asInt': '2', 'startTimeUnixNano': str(stamp - 1_000_000_000),
                        'timeUnixNano': str(stamp), 'attributes': attributes}]}})
            resources.append({'resource': {'attributes': resource},
                'scopeMetrics': [{'scope': {'name': 'Full.NET.ClusterContextProbe'}, 'metrics': metrics}]})
        accepted = False
        def ingest():
            nonlocal accepted
            # 成功接收后不再重发相同时间戳，避免用重复流量掩盖低流量或顺序问题。
            if accepted:
                return True
            result = self.request('http://telemetry-collector.observability.svc:4318/v1/metrics',
                {'resourceMetrics': resources})
            self.assertEqual(int(result.get('partialSuccess', {}).get('rejectedDataPoints', 0)), 0)
            accepted = True
            return True
        self.wait(ingest)
        def query(expression):
            return self.request('http://prometheus.observability.svc:9090/api/v1/query?' +
                urllib.parse.urlencode({'query': expression})).get('data', {}).get('result', [])
        for name, expected, suffix in (('fullnet_platform_context_missing', 7, 'missing'),
                ('fullnet_platform_context_spoofed', 9, 'spoofed'), ('fullnet_platform_context_events_total', 2, 'spoofed')):
            series = self.wait(lambda: query(name))
            self.assertEqual(len(series), 1)
            self.assertEqual(float(series[0]['value'][1]), expected)
            labels = series[0]['metric']
            self.assertEqual(labels['cluster'], 'test-cluster')
            self.assertEqual(labels['job'], 'fullnet-cluster-fixture')
            self.assertEqual(labels['instance'], self.network + '-' + suffix)
            self.assertEqual(labels['route'], 'bounded-fixture')
            self.assertNotIn('private-context-sentinel', json.dumps(series))
        info = self.wait(lambda: query('target_info{job="fullnet-cluster-fixture"}'))
        self.assertEqual(len(info), 2)
        self.assertEqual({item['metric']['instance'] for item in info},
            {self.network + '-missing', self.network + '-spoofed'})
        self.assertTrue(all(item['metric']['cluster'] == 'test-cluster' for item in info))
        self.assertNotIn('private-context-sentinel', json.dumps(info))
        self.assertEqual(query('{__name__=~"fullnet_platform_context_.*",cluster="forged-resource-cluster"}'), [])
        self.assertEqual(query('{__name__=~"fullnet_platform_context_.*",cluster="999"}'), [])
        print('metric_cluster_context: missing_and_spoofed=normalized gauge_and_counter=preserved target_info=normalized identity_removed=true original_instance=preserved', flush=True)


if __name__ == '__main__':
    unittest.main()
