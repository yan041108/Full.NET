"""平台指标渲染边界；真实运行方法复用这里生成的原始配置。"""
import json
import os
from pathlib import Path
import re
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
HELM = os.environ.get('FULLNET_TEST_REAL_HELM', 'helm')
CHART = ROOT / 'deploy/helm/fullnet-observability'
PROMETHEUS = 'prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e'
ALERTMANAGER = 'quay.io/prometheus/alertmanager:v0.34.0@sha256:690c7b525f4367aa91f73e2f91c632206d32e97c6384bdbf2fb7a861b420340d'


def render(*arguments, configured=True):
    command = [HELM, 'template', 'fullnet-observability', str(CHART), '--namespace', 'observability',
        '--set-file', 'rulesSource=' + str(ROOT / 'deploy/observability/prometheus-rules.yaml'),
        '--set-file', 'collectorSource=' + str(ROOT / 'deploy/observability/otel-collector-values.yaml')]
    if configured:
        command += ['--set', 'cluster=test-cluster,prometheusClaim=prometheus-data,alertmanagerClaim=alertmanager-data']
    return subprocess.run([*command, *arguments], capture_output=True, text=True, timeout=30)


def bundle(*arguments):
    result = render(*arguments)
    if result.returncode:
        raise RuntimeError('platform_render_failed: ' + result.stderr[-1500:])
    return json.loads('\n'.join(line for line in result.stdout.splitlines() if line and not line.startswith(('#', '---'))))


def configuration(value):
    data = next(item['data'] for item in value['items'] if item['kind'] == 'ConfigMap')
    return {name: json.loads(content) for name, content in data.items()}


def deployments(value):
    return {item['metadata']['name']: item for item in value['items'] if item['kind'] == 'Deployment'}


class MetricsPlatformTests(unittest.TestCase):
    def test_explicit_cluster_and_storage_are_required(self):
        result = render(configured=False)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('explicit DNS names and storage claims are required', result.stderr)

    def test_renders_platform_only_and_never_creates_secret_or_volume_claim(self):
        value = bundle()
        self.assertEqual(value['kind'], 'List')
        self.assertEqual(set(deployments(value)), {'prometheus', 'alertmanager', 'telemetry-collector', 'kube-state-metrics'})
        self.assertFalse(any(item['kind'] in ('Secret', 'PersistentVolumeClaim', 'ClusterRole', 'ClusterRoleBinding') for item in value['items']))
        self.assertTrue(all(item.get('metadata', {}).get('namespace') != 'fullnet-production' or item['kind'] in ('Role', 'RoleBinding') for item in value['items']))

    def test_default_receiver_discards_and_probe_alert_is_disabled(self):
        configs = configuration(bundle())
        self.assertEqual(configs['alertmanager.json']['receivers'], [{'name': 'discard'}])
        names = [rule.get('alert') for group in configs['rules.json']['groups'] for rule in group['rules']]
        self.assertNotIn('FullNetTelemetryPipelineProbe', names)

    def test_all_existing_alerts_are_retained_without_threshold_changes(self):
        configs = configuration(bundle())
        rules = [rule for group in configs['rules.json']['groups'] for rule in group['rules']]
        original = (ROOT / 'deploy/observability/prometheus-rules.yaml').read_text()
        self.assertEqual({rule['alert'] for rule in rules if 'alert' in rule}, set(re.findall(r'^\s+- alert: (\S+)', original, re.M)))
        no_replicas = next(rule for rule in rules if rule.get('alert') == 'FullNetDeploymentNoAvailableReplicas')
        self.assertEqual(no_replicas['for'], '2m')

    def test_collector_is_metrics_only_with_bounded_existing_wal_and_translation(self):
        config = configuration(bundle())['collector.json']
        self.assertEqual(set(config['service']['pipelines']), {'metrics'})
        self.assertEqual(set(config['exporters']), {'prometheus_remote_write'})
        exporter = config['exporters']['prometheus_remote_write']
        self.assertEqual(exporter['remote_write_queue']['num_consumers'], 1)
        self.assertEqual(exporter['max_batch_request_parallelism'], 1)
        self.assertIn('wal', exporter)
        self.assertEqual(exporter['translation_strategy'], 'UnderscoreEscapingWithSuffixes')
        self.assertEqual(exporter['http']['endpoint'], 'http://prometheus.observability.svc:9090/api/v1/write')

    def test_identity_attributes_are_removed_from_points_and_resources(self):
        config = configuration(bundle())['collector.json']
        point = config['processors']['attributes/drop_high_cardinality']['actions']
        resource = config['processors']['resource/drop_high_cardinality']['attributes']
        expected = {'DiagnosticGroup', 'tenant_id', 'user_id'}
        for actions in (point, resource):
            self.assertTrue(expected.issubset({value['key'] for value in actions if value['action'] == 'delete'}))
        processors = config['service']['pipelines']['metrics']['processors']
        self.assertIn('resource/drop_high_cardinality', processors)
        self.assertIn('attributes/drop_high_cardinality', processors)

    def test_reader_has_only_deployment_read_permission_in_application_namespace(self):
        value = bundle()
        role = next(item for item in value['items'] if item['kind'] == 'Role')
        self.assertEqual(role['metadata']['namespace'], 'fullnet-production')
        self.assertEqual(role['rules'], [{'apiGroups': ['apps'], 'resources': ['deployments'], 'verbs': ['get', 'list', 'watch']}])
        for name, deployment in deployments(value).items():
            pod = deployment['spec']['template']['spec']
            self.assertEqual(pod['automountServiceAccountToken'], name == 'kube-state-metrics')

    def test_components_are_pinned_non_root_and_use_explicit_storage(self):
        for name, deployment in deployments(bundle()).items():
            pod = deployment['spec']['template']['spec']
            self.assertTrue(pod['securityContext']['runAsNonRoot'])
            container = pod['containers'][0]
            self.assertRegex(container['image'], r'@sha256:[a-f0-9]{64}$')
            self.assertTrue(container['securityContext']['readOnlyRootFilesystem'])
            self.assertFalse(container['securityContext']['allowPrivilegeEscalation'])
            if name in ('prometheus', 'alertmanager'):
                self.assertTrue(any('persistentVolumeClaim' in volume for volume in pod['volumes']))
                self.assertEqual(deployment['spec']['strategy']['type'], 'Recreate')

    def test_receiver_requires_https_ca_and_rejects_url_credentials(self):
        for url in ('http://receiver/alerts', 'https://user:password@receiver/alerts', 'https://receiver/alerts?token=secret', 'https://receiver/alerts#fragment'):
            with self.subTest(url=url):
                result = render('--set', 'receiver.enabled=true,receiver.caSecretName=receiver-ca', '--set-string', 'receiver.url=' + url)
                self.assertNotEqual(result.returncode, 0)
        result = render('--set', 'receiver.enabled=true', '--set-string', 'receiver.url=https://receiver/alerts')
        self.assertNotEqual(result.returncode, 0)

    def test_receiver_uses_secret_file_references_and_defaults_to_probe_only(self):
        value = bundle('--set', 'receiver.enabled=true,receiver.caSecretName=receiver-ca,receiver.authorizationSecretName=receiver-token', '--set-string', 'receiver.url=https://receiver/alerts')
        config = configuration(value)['alertmanager.json']
        self.assertEqual(config['route']['receiver'], 'discard')
        self.assertEqual(config['route']['routes'][0]['matchers'], ['alertname="FullNetTelemetryPipelineProbe"'])
        http = config['receivers'][1]['webhook_configs'][0]['http_config']
        self.assertFalse(http['follow_redirects'])
        self.assertEqual(http['tls_config'], {'ca_file': '/etc/alert-ca/ca.pem'})
        self.assertEqual(http['authorization']['credentials_file'], '/etc/alert-auth/token')
        self.assertFalse(any(item['kind'] == 'Secret' for item in value['items']))

    def test_probe_is_explicit_and_has_its_own_threshold(self):
        config = configuration(bundle('--set', 'probeAlert=true'))['rules.json']
        probe = [rule for group in config['groups'] for rule in group['rules'] if rule.get('alert') == 'FullNetTelemetryPipelineProbe']
        self.assertEqual(len(probe), 1)
        self.assertEqual(probe[0]['for'], '10s')
        self.assertEqual(probe[0]['expr'], 'fullnet_pipeline_probe_state == 1')

    def test_shared_claims_or_application_namespace_or_invalid_interval_are_rejected(self):
        for arguments in (['--set', 'alertmanagerClaim=prometheus-data'], ['--namespace', 'fullnet-production'], ['--namespace', 'kube-system'], ['--set', 'evaluationInterval=0s']):
            with self.subTest(arguments=arguments):
                self.assertNotEqual(render(*arguments).returncode, 0)

    def test_notification_switches_reject_string_booleans(self):
        for key in ('receiver.enabled', 'receiver.probeOnly', 'probeAlert'):
            with self.subTest(key=key):
                result = render('--set-string', key + '=false')
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Invalid type', result.stderr)


if __name__ == '__main__':
    unittest.main()
