"""一次性 Linux 运行器的真实 systemd/凭据/Prometheus 验收，不操作发布节点。"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import socket
import stat
import subprocess
import sys
import time
import uuid

from object_capacity_exporter_install_test import ExporterInstallFixture, ROOT, installer
from metrics_platform_test import bundle, configuration, deployments


class CapacityExporterSystemdTests(ExporterInstallFixture):
    def test_real_systemd_credentials_and_exact_scrape_cli(self):
        self.assertEqual(os.environ.get('FULLNET_EXPORTER_SYSTEMD_REAL'), 'isolated')
        actual = installer.execute
        # 真实服务与 host 网络必须位于同一本地引擎；不向远程 Docker 创建资源。
        context = json.loads(actual(['docker', 'context', 'inspect']))[0]['Endpoints']['docker']['Host']
        self.assertTrue(context.startswith('unix://'))
        self.assertIn(os.environ.get('DOCKER_HOST', context), ('', context))
        self.assertEqual(actual(['systemctl', 'show', '-p', 'LoadState', '--value', installer.UNIT]).strip(), b'not-found')
        self.targets['unit'] = Path('/run/systemd/system') / installer.UNIT
        self.assertFalse(os.path.lexists(self.targets['unit']))
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as route:
            route.connect(('192.0.2.1', 9))
            address = route.getsockname()[0]
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
            listener.bind((address, 9100))
        self.options.bind_address = address
        archive = os.environ.get('FULLNET_NODE_EXPORTER_ARCHIVE')
        self.assertTrue(archive)
        shutil.copyfile(archive, self.options.archive)
        extension = self.inputs / 'server.ext'
        extension.write_text('basicConstraints=critical,CA:FALSE\nextendedKeyUsage=serverAuth\nsubjectAltName=IP:' + address + '\n')
        actual(['openssl', 'x509', '-req', '-days', '1', '-in', self.pki / 'server.csr', '-CA', self.pki / 'trusted.pem',
            '-CAkey', self.pki / 'trusted.key', '-CAcreateserial', '-extfile', extension, '-out', self.options.server_certificate])
        actual(['openssl', 'req', '-new', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=prometheus',
            '-keyout', self.inputs / 'client.key', '-out', self.inputs / 'client.csr'])
        extension = self.inputs / 'client.ext'
        extension.write_text('basicConstraints=critical,CA:FALSE\nextendedKeyUsage=clientAuth\nsubjectAltName=DNS:' + self.options.client_san + '\n')
        actual(['openssl', 'x509', '-req', '-days', '1', '-in', self.inputs / 'client.csr', '-CA', self.pki / 'trusted.pem',
            '-CAkey', self.pki / 'trusted.key', '-CAcreateserial', '-extfile', extension, '-out', self.inputs / 'client.crt'])
        publisher_spec = importlib.util.spec_from_file_location('capacity_real_systemd_publisher', ROOT / 'eng/deploy/ubuntu/Verify-ObjectStorageCapacity.py')
        publisher = importlib.util.module_from_spec(publisher_spec)
        publisher_spec.loader.exec_module(publisher)
        publisher.publish_metrics(self.targets['metrics'], {'state': 'growth_headroom_available', 'freeSlots': 24, 'freeBytes': 8 * 1024**3})
        prepared = installer.prepare(self.options, self.targets)
        unit_bytes = prepared[2].encode()
        def cleanup_service():
            unit = self.targets['unit']
            if not os.path.lexists(unit):
                return
            self.assertTrue(stat.S_ISREG(unit.lstat().st_mode) and not unit.is_symlink())
            self.assertEqual(unit.read_bytes(), unit_bytes, '拒绝回收身份改变的服务')
            actual(['systemctl', 'disable', '--now', installer.UNIT])
            self.assertIn(actual(['systemctl', 'show', '-p', 'ActiveState', '--value', installer.UNIT]).strip(), (b'inactive', b'failed'))
            unit.unlink()
            actual(['systemctl', 'daemon-reload'])
            print('capacity_systemd_cleanup: own_service_stopped=true runtime_unit_removed=true', flush=True)
        self.addCleanup(cleanup_service)
        installer.install(prepared, self.targets)
        pid = int(actual(['systemctl', 'show', '-p', 'MainPID', '--value', installer.UNIT]).strip())
        self.assertGreater(pid, 0)
        status = Path('/proc') / str(pid) / 'status'
        fields = dict(line.split(':', 1) for line in status.read_text().splitlines() if ':' in line)
        uid = int(fields['Uid'].split()[0])
        self.assertNotEqual(uid, 0)
        self.assertEqual(int(fields['CapEff'].strip(), 16), 0)
        self.assertEqual((Path('/proc') / str(pid) / 'exe').resolve(), self.targets['binary'] / 'node_exporter')
        value = bundle('--set-json', 'externalScrapes=' + json.dumps([{'job': 'fullnet-object-capacity',
            'targets': [address + ':9100'], 'caSecretName': 'fixture-ca', 'clientCertificateSecretName': 'fixture-client'}]))
        configs = configuration(value)
        config = configs['prometheus.json']
        config['scrape_configs'] = [job for job in config['scrape_configs'] if job['job_name'] == 'fullnet-object-capacity']
        config['rule_files'] = []
        config['alerting'] = {'alertmanagers': []}
        config_directory = self.root / 'prometheus-config'
        config_directory.mkdir(mode=0o755)
        (config_directory / 'prometheus.json').write_text(json.dumps(config))
        (config_directory / 'prometheus.json').chmod(0o644)
        secret_root = self.inputs / 'secrets'
        secret_root.mkdir(mode=0o755)
        for name, files in (('fixture-ca', {'ca.pem': self.options.server_ca}),
                ('fixture-client', {'tls.crt': self.inputs / 'client.crt', 'tls.key': self.inputs / 'client.key'})):
            directory = secret_root / name
            directory.mkdir(mode=0o755)
            for filename, source in files.items():
                shutil.copyfile(source, directory / filename)
                (directory / filename).chmod(0o644)
        prometheus = deployments(value)['prometheus']['spec']['template']['spec']
        container = prometheus['containers'][0]
        result = subprocess.run(['docker', 'pull', container['image']], capture_output=True, text=True, timeout=180)
        self.assertEqual(result.returncode, 0, '固定 Prometheus 镜像准备失败')
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
        name = 'fullnet-capacity-systemd-' + uuid.uuid4().hex[:12]
        def cleanup_prometheus():
            found = subprocess.run(['docker', 'inspect', name], capture_output=True, text=True, timeout=10)
            if found.returncode:
                return
            self.assertEqual(json.loads(found.stdout)[0]['Config']['Labels'].get('fullnet.capacity.test'), name)
            actual(['docker', 'rm', '-f', name])
            self.assertEqual(actual(['docker', 'ps', '-aq', '--filter', 'label=fullnet.capacity.test=' + name]).strip(), b'')
            print('capacity_prometheus_cleanup: own_containers=0', flush=True)
        self.addCleanup(cleanup_prometheus)
        arguments = ['docker', 'run', '-d', '--name', name, '--label', 'fullnet.capacity.test=' + name,
            '--network', 'host', '--read-only', '--user', '65534:65534', '--cap-drop=ALL', '--security-opt=no-new-privileges',
            '--cpus', '0.5', '--memory', '256m', '--pids-limit', '128', '--tmpfs', '/prometheus:uid=65534,gid=65534,mode=0700,size=64m',
            '--mount', f'type=bind,source={config_directory},target=/etc/fullnet,readonly']
        secrets = {volume['name']: volume['secret']['secretName'] for volume in prometheus['volumes'] if 'secret' in volume}
        for mount in container['volumeMounts']:
            if mount['name'] in secrets:
                arguments += ['--mount', f'type=bind,source={secret_root / secrets[mount["name"]]},target={mount["mountPath"]},readonly']
        actual([*arguments, container['image'], *container['args'], '--web.listen-address=127.0.0.1:' + str(port)])
        cli = [sys.executable, '-O', str(ROOT / 'eng/deploy/ubuntu/Verify-ObjectCapacityScrape.py'),
            '--prometheus-url', f'http://127.0.0.1:{port}', '--cluster', 'test-cluster', '--instance', address + ':9100']
        def wait_report(state, code):
            deadline = time.monotonic() + 45
            last = None
            while time.monotonic() < deadline:
                check = subprocess.run(cli, capture_output=True, text=True, timeout=10)
                self.assertEqual(check.stderr, '')
                last = json.loads(check.stdout)
                if check.returncode == code and last.get('state') == state:
                    return last
                time.sleep(.5)
            self.fail('实际验收入口未观察到预期状态：' + repr(last))
        available = wait_report('growth_headroom_available', 0)
        self.assertEqual(available['freeSlots'], 24)
        publisher.publish_metrics(self.targets['metrics'], {'state': 'growth_blocked', 'freeSlots': 0, 'freeBytes': 8 * 1024**3})
        blocked = wait_report('growth_blocked', 1)
        self.assertEqual(blocked['freeSlots'], 0)
        publisher.publish_metrics(self.targets['metrics'], {'state': 'probe_unavailable'})
        unavailable = wait_report('probe_unavailable', 2)
        self.assertNotIn('freeSlots', unavailable)
        self.assertEqual(actual(['systemctl', 'show', '-p', 'MainPID', '--value', installer.UNIT]).strip(), str(pid).encode())
        print(f'capacity_systemd: dynamic_uid={uid} effective_capabilities=0 credentials_mtls=accepted cli=available:0,blocked:1,unavailable:2 production_install=false', flush=True)
