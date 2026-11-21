"""外部指标只接受明确静态目标，Secret 仅以投影引用进入 Prometheus。"""
import json
import unittest

from metrics_platform_test import bundle, configuration, deployments, render


class ExternalScrapeTests(unittest.TestCase):
    def test_default_has_no_external_targets_or_secret_projections(self):
        value = bundle()
        jobs = configuration(value)['prometheus.json']['scrape_configs']
        self.assertEqual({job['job_name'] for job in jobs}, {'fullnet-kube-state-metrics', 'fullnet-collector'})
        pod = deployments(value)['prometheus']['spec']['template']['spec']
        self.assertFalse(any('secret' in volume for volume in pod['volumes']))

    def test_explicit_jobs_preserve_labels_budgets_paths_and_prometheus_only_secret_references(self):
        scrapes = [
            {'job': 'fullnet-object-capacity', 'targets': ['storage.metrics.svc:9100'],
                'caSecretName': 'exporter-ca', 'authorizationSecretName': 'exporter-token'},
            {'job': 'fluent-bit', 'targets': ['logs.metrics.svc:2020'], 'scheme': 'http',
                'metricsPath': '/api/v1/metrics/prometheus'},
            {'job': 'waf', 'targets': ['edge.metrics.svc:9443']}]
        value = bundle('--set-json', 'externalScrapes=' + json.dumps(scrapes))
        jobs = {entry['job_name']: entry for entry in configuration(value)['prometheus.json']['scrape_configs']}
        self.assertEqual(len(jobs), 5)
        capacity = jobs['fullnet-object-capacity']
        self.assertEqual(capacity['scheme'], 'https')
        self.assertEqual(capacity['static_configs'], [{'targets': ['storage.metrics.svc:9100'], 'labels': {'cluster': 'test-cluster'}}])
        self.assertEqual(capacity['authorization'], {'type': 'Bearer',
            'credentials_file': '/etc/fullnet-scrapes/fullnet-object-capacity/auth/token'})
        self.assertEqual(capacity['tls_config'], {'insecure_skip_verify': False,
            'ca_file': '/etc/fullnet-scrapes/fullnet-object-capacity/ca/ca.pem'})
        self.assertEqual(jobs['waf']['tls_config'], {'insecure_skip_verify': False})
        self.assertEqual(jobs['fluent-bit']['metrics_path'], '/api/v1/metrics/prometheus')
        self.assertNotIn('authorization', jobs['fluent-bit'])
        for job in scrapes:
            actual = jobs[job['job']]
            self.assertFalse(actual['follow_redirects'])
            self.assertFalse(actual['honor_labels'])
            self.assertFalse(actual['honor_timestamps'])
            self.assertEqual(actual['scrape_timeout'], '5s')
            self.assertEqual(actual['scrape_interval'], '15s')
            self.assertEqual(actual['sample_limit'], 20000)
            self.assertEqual(actual['body_size_limit'], '5MB')
        for name, deployment in deployments(value).items():
            pod = deployment['spec']['template']['spec']
            secrets = [volume['secret'] for volume in pod['volumes'] if 'secret' in volume]
            if name == 'prometheus':
                self.assertEqual({secret['secretName'] for secret in secrets}, {'exporter-ca', 'exporter-token'})
                self.assertTrue(all(secret['defaultMode'] == 288 for secret in secrets))
                self.assertFalse(pod['automountServiceAccountToken'])
            else:
                self.assertEqual(secrets, [])
        self.assertFalse(any(item['kind'] == 'Secret' for item in value['items']))

    def test_unknown_jobs_unsafe_targets_credentials_and_unbounded_inputs_are_rejected(self):
        valid = {'job': 'fullnet-object-capacity', 'targets': ['source.metrics.svc:9100']}
        invalid = [
            [{**valid, 'job': 'arbitrary'}], [valid, valid],
            [{**valid, 'targets': []}], [{**valid, 'targets': valid['targets'] * 2}],
            [{**valid, 'targets': [f'source{i}:9100' for i in range(9)]}],
            [{**valid, 'scheme': 'ftp'}], [{**valid, 'scheme': False}],
            [{**valid, 'metricsPath': '/admin'}], [{**valid, 'caSecretName': '../ca'}],
            [{**valid, 'authorization': 'inline-secret'}], [{**valid, 'insecure_skip_verify': True}],
            [{**valid, 'scheme': 'http', 'authorizationSecretName': 'token'}],
            [{**valid, 'scheme': 'http', 'caSecretName': 'ca'}], valid]
        invalid += [[{**valid, 'targets': [target]}] for target in (
            'https://source:9100', 'user:password@source:9100', 'source:9100/metrics',
            'source:9100?token=secret', 'source:0', 'source:65536', 'source:99999',
            'source..metrics:9100', 'source:9100;command', 'source:9100 other:9100')]
        for scrapes in invalid:
            with self.subTest(case=invalid.index(scrapes)):
                result = render('--set-json', 'externalScrapes=' + json.dumps(scrapes))
                self.assertNotEqual(result.returncode, 0)


if __name__ == '__main__':
    unittest.main()
