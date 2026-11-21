"""应用指标出口使用独立、显式且精确的 NetworkPolicy；不改变既有网络默认值。"""
import json
import os
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
CHART = ROOT / 'deploy/helm/fullnet'


def render(role='api', *arguments, template=None, release=None):
    command = [os.environ.get('FULLNET_TEST_REAL_HELM', 'helm'), 'template', release or 'metrics-' + role, str(CHART),
        '--namespace', 'fullnet-production', '-f', str(CHART / f'ci/values-role-{role}.yaml'),
        '-f', str(CHART / 'ci/values-provider-sqlserver.yaml')]
    if template:
        command += ['--show-only', 'templates/' + template]
    return subprocess.run([*command, *arguments], capture_output=True, text=True, timeout=15)


def policy(role='api', *arguments):
    result = render(role, '--set-string', 'networkPolicy.metricsNamespace=observability', *arguments, template='metrics-egress.yaml')
    if result.returncode:
        raise RuntimeError(result.stderr[-2000:])
    return json.loads('\n'.join(line for line in result.stdout.splitlines() if line.strip() and not line.startswith(('#', '---'))))


class MetricsNetworkEgressTests(unittest.TestCase):
    def test_default_adds_no_metrics_policy_and_keeps_original_network_policy(self):
        for role in ('api', 'worker', 'migrator'):
            default = render(role)
            self.assertEqual(default.returncode, 0, default.stderr)
            self.assertNotIn('-metrics-egress', default.stdout)
            original = render(role, template='networkpolicy.yaml')
            configured = render(role, '--set-string', 'networkPolicy.metricsNamespace=observability', template='networkpolicy.yaml')
            self.assertEqual((original.returncode, configured.returncode), (0, 0))
            self.assertEqual(original.stdout, configured.stdout)

    def test_api_and_worker_allow_only_exact_collector_namespace_and_otlp_ports(self):
        for role in ('api', 'worker'):
            value = policy(role)
            self.assertEqual(value['metadata']['namespace'], 'fullnet-production')
            self.assertEqual(value['spec']['policyTypes'], ['Egress'])
            self.assertEqual(value['spec']['podSelector'], {'matchLabels': {'app.kubernetes.io/name': 'fullnet',
                'app.kubernetes.io/instance': 'metrics-' + role}, 'matchExpressions': [{'key': 'app.kubernetes.io/component',
                    'operator': 'In', 'values': ['api', 'worker']}]})
            self.assertEqual(value['spec']['egress'], [{'to': [{'namespaceSelector': {'matchLabels': {
                'kubernetes.io/metadata.name': 'observability'}}, 'podSelector': {'matchLabels': {
                    'app.kubernetes.io/part-of': 'fullnet-observability', 'app.kubernetes.io/name': 'telemetry-collector'}}}],
                'ports': [{'protocol': 'TCP', 'port': 4317}, {'protocol': 'TCP', 'port': 4318}]}])

    def test_namespace_override_and_long_names_do_not_collide_with_original_policy(self):
        value = policy('worker', '--set-string', 'networkPolicy.metricsNamespace=metrics-platform',
            '--set-string', 'fullnameOverride=' + 'x' * 63)
        self.assertLessEqual(len(value['metadata']['name']), 63)
        self.assertTrue(value['metadata']['name'].endswith('-metrics-egress'))
        self.assertNotEqual(value['metadata']['name'], 'x' * 63)
        self.assertEqual(value['spec']['egress'][0]['to'][0]['namespaceSelector']['matchLabels'],
            {'kubernetes.io/metadata.name': 'metrics-platform'})

    def test_migrator_never_gets_metrics_egress_even_with_shared_values(self):
        result = render('migrator', '--set-string', 'networkPolicy.metricsNamespace=observability')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn('-metrics-egress', result.stdout)

    def test_invalid_namespace_types_and_disabled_policy_are_rejected(self):
        for arguments in (('--set', 'networkPolicy.metricsNamespace=true'),
                ('--set-string', 'networkPolicy.metricsNamespace=../observability'),
                ('--set-string', 'networkPolicy.metricsNamespace=UPPER'),
                ('--set-string', 'networkPolicy.metricsNamespace=' + 'x' * 64),
                ('--set-string', 'networkPolicy.metricsNamespace=observability', '--set', 'networkPolicy.enabled=false')):
            with self.subTest(arguments=arguments):
                self.assertNotEqual(render('api', *arguments).returncode, 0)


if __name__ == '__main__':
    unittest.main()
