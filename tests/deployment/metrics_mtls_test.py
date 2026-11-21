"""外部抓取客户端证书只能使用 HTTPS，证书与私钥只投影到 Prometheus。"""
import json
import unittest

from metrics_platform_test import bundle, configuration, deployments, render


class MetricsMtlsTests(unittest.TestCase):
    def test_client_certificate_uses_only_read_only_secret_files(self):
        sources = [{'job': 'fullnet-object-capacity', 'targets': ['storage.internal:9100'],
            'caSecretName': 'exporter-ca', 'clientCertificateSecretName': 'exporter-client'}]
        value = bundle('--set-json', 'externalScrapes=' + json.dumps(sources))
        entry = next(item for item in configuration(value)['prometheus.json']['scrape_configs']
            if item['job_name'] == 'fullnet-object-capacity')
        self.assertEqual(entry['tls_config'], {'insecure_skip_verify': False,
            'ca_file': '/etc/fullnet-scrapes/fullnet-object-capacity/ca/ca.pem',
            'cert_file': '/etc/fullnet-scrapes/fullnet-object-capacity/client/tls.crt',
            'key_file': '/etc/fullnet-scrapes/fullnet-object-capacity/client/tls.key'})
        for name, deployment in deployments(value).items():
            pod = deployment['spec']['template']['spec']
            client = [volume for volume in pod['volumes'] if volume.get('secret', {}).get('secretName') == 'exporter-client']
            if name == 'prometheus':
                self.assertEqual(len(client), 1)
                self.assertEqual(client[0]['secret']['defaultMode'], 288)
                self.assertEqual(client[0]['secret']['items'], [{'key': 'tls.crt', 'path': 'tls.crt'}, {'key': 'tls.key', 'path': 'tls.key'}])
                self.assertTrue(next(mount for mount in pod['containers'][0]['volumeMounts']
                    if mount['name'] == client[0]['name'])['readOnly'])
            else:
                self.assertEqual(client, [])
        self.assertFalse(any(item['kind'] == 'Secret' for item in value['items']))

    def test_mtls_cannot_use_plain_http_or_inline_credentials(self):
        base = {'job': 'fullnet-object-capacity', 'targets': ['storage.internal:9100']}
        for change in ({'scheme': 'http', 'clientCertificateSecretName': 'client'},
                {'clientCertificateSecretName': '../client'}, {'clientCertificateSecretName': True},
                {'clientKey': 'inline-private-key'}, {'tls_config': {'insecure_skip_verify': True}}):
            with self.subTest(change=change):
                self.assertNotEqual(render('--set-json', 'externalScrapes=' + json.dumps([{**base, **change}])).returncode, 0)


if __name__ == '__main__':
    unittest.main()
