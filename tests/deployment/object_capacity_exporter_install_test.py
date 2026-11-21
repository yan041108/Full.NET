"""首次安装的文件、凭据与失败恢复边界；systemctl 使用受控执行器，不改宿主服务。"""
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('capacity_exporter_installer', ROOT / 'eng/deploy/ubuntu/Install-ObjectCapacityExporter.py')
installer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(installer)


def archive_fixture(changes=()):
    output = io.BytesIO()
    binary = b'\x7fELF\x02\x01' + bytes(12) + b'\x3e\x00' + bytes(44)
    entries = [('node_exporter', binary, tarfile.REGTYPE), ('LICENSE', b'license', tarfile.REGTYPE),
        ('NOTICE', b'notice', tarfile.REGTYPE), *changes]
    with tarfile.open(fileobj=output, mode='w:gz') as archive:
        for name, content, kind in entries:
            member = tarfile.TarInfo(f'node_exporter-{installer.VERSION}.linux-amd64/' + name)
            member.type = kind
            member.size = len(content) if kind == tarfile.REGTYPE else 0
            member.linkname = '/etc/passwd' if kind != tarfile.REGTYPE else ''
            archive.addfile(member, io.BytesIO(content) if kind == tarfile.REGTYPE else None)
    return output.getvalue()


class ExporterInstallFixture(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if os.environ.get('FULLNET_EXPORTER_INSTALL_TEST') != 'isolated' or os.geteuid() != 0:
            raise RuntimeError('仅允许显式隔离的 Linux root 夹具，不修改宿主服务')
        cls.materials = tempfile.TemporaryDirectory(prefix='fullnet-exporter-pki-', dir='/var/lib')
        cls.addClassCleanup(cls.materials.cleanup)
        cls.pki = Path(cls.materials.name)
        for name in ('trusted', 'untrusted'):
            installer.execute(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                '-subj', '/CN=' + name, '-addext', 'basicConstraints=critical,CA:TRUE',
                '-keyout', cls.pki / (name + '.key'), '-out', cls.pki / (name + '.pem')])
        installer.execute(['openssl', 'req', '-new', '-newkey', 'rsa:2048', '-nodes', '-subj', '/CN=exporter',
            '-keyout', cls.pki / 'server.key', '-out', cls.pki / 'server.csr'])
        (cls.pki / 'server.ext').write_text('basicConstraints=critical,CA:FALSE\nextendedKeyUsage=serverAuth\nsubjectAltName=IP:10.42.0.10\n')
        installer.execute(['openssl', 'x509', '-req', '-days', '1', '-in', cls.pki / 'server.csr',
            '-CA', cls.pki / 'trusted.pem', '-CAkey', cls.pki / 'trusted.key', '-CAcreateserial',
            '-extfile', cls.pki / 'server.ext', '-out', cls.pki / 'server.crt'])
        cls.credentials = {name: (cls.pki / source).read_bytes() for name, source in (
            ('server.crt', 'server.crt'), ('server.key', 'server.key'),
            ('server-ca.pem', 'trusted.pem'), ('client-ca.pem', 'trusted.pem'))}

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-exporter-install-', dir='/var/lib')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.root.chmod(0o755)
        self.targets = installer.paths(self.root)
        for target in self.targets.values():
            target.parent.mkdir(parents=True, exist_ok=True)
        self.targets['metrics'].mkdir(mode=0o755)
        self.inputs = self.root / 'inputs'
        self.inputs.mkdir(mode=0o700)
        for name, content in self.credentials.items():
            (self.inputs / name).write_bytes(content)
            (self.inputs / name).chmod(0o600)
        self.archive = archive_fixture()
        (self.inputs / 'exporter.tar.gz').write_bytes(self.archive)
        self.options = SimpleNamespace(archive=self.inputs / 'exporter.tar.gz', bind_address='10.42.0.10',
            client_san='prometheus.observability.svc', server_certificate=self.inputs / 'server.crt',
            server_key=self.inputs / 'server.key', server_ca=self.inputs / 'server-ca.pem', client_ca=self.inputs / 'client-ca.pem')
        self.calls = []
        self.active = False
        self.enabled = False
        self.fail_at = None
        self.stop_confirmed = True
        self.loaded = b'not-found'

    def command(self, arguments, content=None):
        arguments = [str(value) for value in arguments]
        self.calls.append(arguments)
        if arguments[:2] == ['systemctl', 'enable']:
            self.active = self.enabled = True
            if self.fail_at == 'enable':
                raise installer.InstallFailure('command_failed')
            if self.fail_at == 'signal':
                raise KeyboardInterrupt()
        if arguments[:2] == ['systemctl', 'disable']:
            self.enabled = False
            self.active = not self.stop_confirmed
        if 'LoadState' in arguments:
            return self.loaded
        if 'ActiveState' in arguments:
            return b'active' if self.active else b'inactive'
        if 'is-enabled' in arguments:
            return b'enabled' if self.enabled and self.fail_at != 'enabled_check' else b'disabled'
        return b''

    def prepared(self):
        return ({'node_exporter': b'executable', 'LICENSE': b'license', 'NOTICE': b'notice'},
            {'server.crt': b'certificate', 'server.key': b'fixture-only-private-key',
                'client-ca.pem': b'ca', 'web.json': b'{}'}, installer.service_unit('10.42.0.10', self.targets))

    def assert_removed(self):
        for name in ('unit', 'config', 'binary'):
            self.assertFalse(os.path.lexists(self.targets[name]), name)
        self.assertTrue(self.targets['metrics'].is_dir())


class ExporterInstallTests(ExporterInstallFixture):
    def test_digest_is_pinned_before_archive_processing(self):
        with self.assertRaisesRegex(installer.InstallFailure, 'archive_digest_invalid'):
            installer.unpack_archive(self.archive)

    def test_archive_rejects_extra_path_links_and_duplicates(self):
        for change in (('../escape', b'escape', tarfile.REGTYPE), ('link', b'', tarfile.SYMTYPE),
                ('node_exporter', b'duplicate', tarfile.REGTYPE)):
            content = archive_fixture([change])
            with self.subTest(change=change[0]), patch.object(installer, 'ARCHIVE_SHA256', hashlib.sha256(content).hexdigest()):
                with self.assertRaises(installer.InstallFailure):
                    installer.unpack_archive(content)
        with patch.object(installer, 'ARCHIVE_SHA256', hashlib.sha256(self.archive).hexdigest()):
            self.assertEqual(set(installer.unpack_archive(self.archive)), {'node_exporter', 'LICENSE', 'NOTICE'})

    def test_local_inputs_reject_links_writable_keys_and_oversized_files(self):
        source = self.inputs / 'server.key'
        linked = self.inputs / 'linked'
        linked.symlink_to(source)
        with self.assertRaises(OSError):
            installer.read_input(linked)
        linked.unlink()
        os.link(source, linked)
        with self.assertRaisesRegex(installer.InstallFailure, 'permissions'):
            installer.read_input(source)
        linked.unlink()
        source.chmod(0o644)
        with self.assertRaisesRegex(installer.InstallFailure, 'permissions'):
            installer.read_input(source, private=True)
        with self.assertRaisesRegex(installer.InstallFailure, 'size'):
            installer.read_input(source, maximum=1)

    def test_prepare_freezes_valid_credentials_and_never_calls_systemctl(self):
        with patch.object(installer, 'ARCHIVE_SHA256', hashlib.sha256(self.archive).hexdigest()):
            prepared = installer.prepare(self.options, self.targets)
        self.assert_removed()
        web = json.loads(prepared[1]['web.json'])['tls_server_config']
        self.assertEqual(web['client_auth_type'], 'RequireAndVerifyClientCert')
        self.assertEqual(web['client_allowed_sans'], ['prometheus.observability.svc'])
        self.assertNotIn('server-ca.pem', prepared[1])
        self.assertIn('DynamicUser=yes', prepared[2])
        self.assertIn('--web.config.file=%d/web.json', prepared[2])
        self.assertNotIn('fixture-only-private-key', prepared[2])

    def test_address_and_client_identity_reject_wildcards_injection_and_public_binding(self):
        for address in ('0.0.0.0', '127.0.0.1', '169.254.1.1', '8.8.8.8', '::1', '10.42.0.10\nRestart=no'):
            self.options.bind_address = address
            with self.subTest(address=address), self.assertRaises((installer.InstallFailure, ValueError)):
                installer.prepare(self.options, self.targets)
        self.options.bind_address = '10.42.0.10'
        for san in ('*.internal', 'prometheus\nkey=secret', 'https://prometheus', 'x' * 64 + '.internal'):
            self.options.client_san = san
            with self.subTest(san=san), self.assertRaises(installer.InstallFailure):
                installer.prepare(self.options, self.targets)

    def test_actual_openssl_rejects_wrong_ca_ip_key_and_non_ca_client_trust(self):
        installer.validate_credentials(self.credentials, '10.42.0.10')
        for change, address in (({}, '10.42.0.11'),
                ({'server-ca.pem': (self.pki / 'untrusted.pem').read_bytes()}, '10.42.0.10'),
                ({'server.key': (self.pki / 'trusted.key').read_bytes()}, '10.42.0.10'),
                ({'client-ca.pem': self.credentials['server.crt']}, '10.42.0.10')):
            with self.subTest(change=list(change), address=address), self.assertRaises(installer.InstallFailure):
                installer.validate_credentials({**self.credentials, **change}, address)

    def test_existing_files_and_symlinks_are_never_overwritten(self):
        for name in ('config', 'binary', 'unit'):
            self.targets[name].symlink_to(self.inputs)
            with self.subTest(name=name), self.assertRaisesRegex(installer.InstallFailure, 'existing_installation'):
                installer.inspect_targets(self.targets)
            self.assertTrue(self.targets[name].is_symlink())
            self.targets[name].unlink()

    def test_untrusted_parents_and_unreadable_metrics_are_rejected(self):
        self.targets['metrics'].chmod(0o700)
        with self.assertRaisesRegex(installer.InstallFailure, 'metrics_or_binary_directory_unreadable'):
            installer.inspect_targets(self.targets)
        self.targets['metrics'].chmod(0o755)
        self.targets['config'].parent.chmod(0o777)
        with self.assertRaisesRegex(installer.InstallFailure, 'installation_parent_invalid'):
            installer.inspect_targets(self.targets)

    def test_install_uses_private_credentials_and_only_textfile_service(self):
        original_mask = os.umask(0o077)
        try:
            with patch.object(installer, 'execute', self.command):
                installer.install(self.prepared(), self.targets)
        finally:
            os.umask(original_mask)
        self.assertTrue(self.active and self.enabled)
        self.assertEqual(self.targets['binary'].stat().st_mode & 0o777, 0o755)
        self.assertEqual(self.targets['config'].stat().st_mode & 0o777, 0o700)
        self.assertEqual((self.targets['config'] / 'server.key').stat().st_mode & 0o777, 0o600)
        unit = self.targets['unit'].read_text()
        for option in ('DynamicUser=yes', 'ProtectSystem=strict', '--collector.disable-defaults',
                '--web.listen-address=10.42.0.10:9100', 'CapabilityBoundingSet=', 'LoadCredential=server.key:'):
            self.assertIn(option, unit)
        self.assertNotIn('fixture-only-private-key', unit)
        self.assertLess(self.calls.index(['systemd-analyze', 'verify', str(self.targets['unit'])]),
            self.calls.index(['systemctl', 'enable', '--now', installer.UNIT]))

    def test_unknown_enable_response_failed_check_and_interrupt_stop_before_cleanup(self):
        for failure in ('enable', 'enabled_check', 'signal'):
            self.fail_at = failure
            with self.subTest(failure=failure), patch.object(installer, 'execute', self.command):
                with self.assertRaises((installer.InstallFailure, KeyboardInterrupt)):
                    installer.install(self.prepared(), self.targets)
            self.assertFalse(self.active or self.enabled)
            self.assertIn(['systemctl', 'disable', '--now', installer.UNIT], self.calls)
            self.assert_removed()

    def test_unconfirmed_stop_preserves_files_for_recovery(self):
        self.fail_at = 'enable'
        self.stop_confirmed = False
        with patch.object(installer, 'execute', self.command), self.assertRaisesRegex(installer.InstallFailure, 'rollback_incomplete_preserved'):
            installer.install(self.prepared(), self.targets)
        self.assertTrue(self.active)
        self.assertTrue(self.targets['unit'].is_file())
        self.assertTrue((self.targets['config'] / 'server.key').is_file())

    def test_loaded_vendor_unit_is_rejected_before_writing(self):
        self.loaded = b'loaded'
        with patch.object(installer, 'execute', self.command), self.assertRaisesRegex(installer.InstallFailure, 'existing_systemd_unit'):
            installer.install(self.prepared(), self.targets)
        self.assert_removed()

    def test_partial_write_is_removed_without_touching_existing_files(self):
        partial = self.inputs / 'partial'
        with patch.object(installer.os, 'fsync', side_effect=OSError('fixture write failure')), self.assertRaises(OSError):
            installer.write_exclusive(partial, b'incomplete', 0o600)
        self.assertFalse(partial.exists())
        existing = self.inputs / 'server.key'
        with self.assertRaises(FileExistsError):
            installer.write_exclusive(existing, b'replacement', 0o600)
        self.assertEqual(existing.read_bytes(), self.credentials['server.key'])

    def test_busy_lock_is_rejected_before_service_or_output_changes(self):
        with patch.object(installer.fcntl, 'flock', side_effect=BlockingIOError), self.assertRaisesRegex(installer.InstallFailure, 'installation_busy'):
            installer.install(self.prepared(), self.targets)
        self.assertEqual(self.calls, [])
        self.assert_removed()


class ExporterOfficialPackageTests(ExporterInstallFixture):
    def test_official_archive_and_actual_systemd_verify(self):
        source = os.environ.get('FULLNET_NODE_EXPORTER_ARCHIVE')
        self.assertTrue(source, '真实归档场景必须显式提供官方包')
        shutil.copyfile(source, self.options.archive)
        prepared = installer.prepare(self.options, self.targets)
        actual_execute = installer.execute
        self.assertEqual(actual_execute(['systemctl', 'show', '-p', 'LoadState', '--value', installer.UNIT]).strip(), b'not-found')
        def selected(arguments, content=None):
            if str(arguments[0]) == 'systemd-analyze':
                return actual_execute(arguments, content)
            return self.command(arguments, content)
        with patch.object(installer, 'execute', selected):
            installer.install(prepared, self.targets)
        result = installer.subprocess.run([self.targets['binary'] / 'node_exporter', '--version'],
            capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0)
        version = result.stdout + result.stderr
        self.assertIn('version 1.13.1', version)
        self.assertEqual(hashlib.sha256(self.options.archive.read_bytes()).hexdigest(), installer.ARCHIVE_SHA256)
        self.assertEqual((self.targets['config'] / 'server.key').read_bytes(), self.credentials['server.key'])
        print('exporter_install: official_archive=verified actual_binary=1.13.1 systemd_unit=verified service_controller=fixture production_install=false', flush=True)


if __name__ == '__main__':
    unittest.main()
