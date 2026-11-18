"""仅在一次性 Linux 容器中验证备份生成与只读校验；数据库和 systemd 均使用受控替身。"""
import contextlib
import fcntl
import gzip
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import shutil
import signal
import sqlite3
import subprocess
import sys
import tempfile
import time
import unittest


REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("backup_verifier", REPOSITORY / "eng/deploy/ubuntu/Verify-InfrastructureBackup.py")
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)


STUB = r'''#!/usr/bin/env python3
import json,os,pathlib,sys,time
name=pathlib.Path(sys.argv[0]).name
file=pathlib.Path(os.environ['FULLNET_BACKUP_TEST_STATE'])
state=json.loads(file.read_text())
with open(os.environ['FULLNET_BACKUP_TEST_LOG'],'a') as log: log.write(name+' '+' '.join(sys.argv[1:])+'\n')
def save(): file.write_text(json.dumps(state))
if name=='systemctl':
    if sys.argv[1]=='show': print(state['service'])
    elif sys.argv[1]=='stop':
        if state.get('stop_failure'): sys.exit(1)
        if not state.get('false_stop'): state['service']='inactive';save()
    elif sys.argv[1]=='start':
        state['starts']=state.get('starts',0)+1;save()
        if state.get('start_failure'): sys.exit(1)
        state['service']='active';save()
elif name=='mysqldump':
    if state.get('dump_failure'): print('partial');sys.exit(1)
    print("-- CHANGE REPLICATION SOURCE TO SOURCE_LOG_FILE='binlog.000123', SOURCE_LOG_POS=456;")
    print('CREATE DATABASE fixture;')
elif name=='mysql':
    print('binlog.999999\t999999')
elif name=='redis-cli':
    pathlib.Path(sys.argv[sys.argv.index('--rdb')+1]).write_bytes(b'REDIS-FIXTURE\n')
    if state.get('redis_failure'): sys.exit(1)
elif name=='redis-check-rdb':
    if state.get('redis_check_failure'): sys.exit(1)
    print('fixture checksum ok')
elif name=='dpkg-query': print('fixture-package\t1.0')
elif name=='df':
    print('Filesystem 1024-blocks Used Available Capacity Mounted on')
    print('fixture 999999 100 1024 1% /') if state.get('low_disk') else print('fixture 999999 100 900000 1% /')
elif name=='du': print('6000\tfixture') if state.get('over_budget') else print('1\tfixture')
elif name=='tar':
    if any('object-storage.tar.gz' in arg for arg in sys.argv):
        if state.get('tar_failure'): sys.exit(2)
        if state.get('wait_for_signal'):
            pathlib.Path(os.environ['FULLNET_BACKUP_TEST_WAIT']).touch()
            time.sleep(30)
    os.execv('/bin/tar',['/bin/tar']+sys.argv[1:])
'''


class BackupGenerationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # 不允许在宿主 root 下准备绝对路径；只使用专用、无网络的一次性容器。
        if os.geteuid() != 0 or not pathlib.Path('/.dockerenv').exists() or os.environ.get('FULLNET_BACKUP_TEST_CONTAINER') != 'isolated':
            raise RuntimeError('备份生成测试必须由专用隔离容器入口运行')
        roots = ['/etc/fullnet', '/etc/redis', '/usr/local/lib/fullnet', '/srv/fullnet', '/var/backups/fullnet', '/var/lib/rancher/k3s']
        if any(pathlib.Path(root).exists() for root in roots):
            raise RuntimeError('隔离容器已经含有同名基础设施目录，拒绝接管')
        for root in roots:
            pathlib.Path(root).mkdir(parents=True, mode=0o700)
        for directory in ['/etc/fullnet/secrets', '/etc/mysql', '/var/lib/mysql', '/etc/systemd/system', '/etc/apt/preferences.d']:
            pathlib.Path(directory).mkdir(parents=True, exist_ok=True)
        for name in ['/etc/redis/fixture.conf', '/etc/mysql/fixture.cnf', '/var/lib/mysql/fixture.pem',
                     '/etc/apt/sources.list.d/fullnet-fixture.list', '/etc/apt/preferences.d/fullnet-fixture']:
            pathlib.Path(name).write_text('fixture', encoding='utf-8')
        shutil.copyfile(REPOSITORY / 'eng/deploy/ubuntu/Verify-InfrastructureBackup.py', '/usr/local/lib/fullnet/Verify-InfrastructureBackup.py')
        pathlib.Path('/usr/local/lib/fullnet/Verify-InfrastructureBackup.py').chmod(0o700)
        pathlib.Path('/etc/fullnet/secrets/redis.env').write_text('REDIS_ADMIN_PASSWORD=secret-sentinel\nREDIS_APP_PASSWORD=application-sentinel\n')
        pathlib.Path('/etc/fullnet/secrets/redis.env').chmod(0o600)
        cls.backups = pathlib.Path('/var/backups/fullnet')
        cls.source = REPOSITORY / 'eng/deploy/ubuntu/Backup-Infrastructure.sh'

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-backup-generation-')
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.state = self.root / 'state.json'
        self.events = self.root / 'events.log'
        self.wait = self.root / 'waiting'
        self.events.write_text('')
        self.state.write_text(json.dumps({'service': 'active'}))
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        for name in ['systemctl', 'mysqldump', 'mysql', 'redis-cli', 'redis-check-rdb', 'dpkg-query', 'df', 'du', 'tar']:
            executable = self.bin / name
            executable.write_text(STUB, encoding='utf-8')
            executable.chmod(0o700)
        self.environment = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ['PATH'],
            FULLNET_BACKUP_TEST_STATE=str(self.state), FULLNET_BACKUP_TEST_LOG=str(self.events),
            FULLNET_BACKUP_TEST_WAIT=str(self.wait), PYTHONOPTIMIZE='1')
        pathlib.Path('/etc/fullnet/infrastructure-installed').write_text('role=secondary\n')
        self.data = pathlib.Path('/srv/fullnet/object-storage')
        self.data.mkdir(exist_ok=True)
        (self.data / 'fixture-object').write_text('original fixture bytes')
        self.before = set(self.backups.iterdir())

    def configure(self, **values):
        state = json.loads(self.state.read_text())
        state.update(values)
        self.state.write_text(json.dumps(state))

    def execute(self, role='secondary'):
        pathlib.Path('/etc/fullnet/infrastructure-installed').write_text('role=' + role + '\n')
        result = subprocess.run(['bash', str(self.source)], env=self.environment, capture_output=True, text=True, timeout=25)
        self.assertNotIn('secret-sentinel', result.stdout + result.stderr)
        return result

    def destination(self):
        created = set(self.backups.iterdir()) - self.before
        self.assertEqual(1, len(created))
        return created.pop()

    def assert_failure(self, result):
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.destination() / 'COMPLETE').exists())

    def test_running_storage_is_archived_and_restored_before_completion(self):
        result = self.execute()
        self.assertEqual(0, result.returncode, result.stderr)
        destination = self.destination()
        self.assertTrue((destination / 'object-storage.tar.gz').is_file())
        self.assertEqual('active', json.loads(self.state.read_text())['service'])
        self.assertEqual('complete', verifier.verify(destination, 'secondary')['state'])
        self.assertEqual(1, self.events.read_text().count('systemctl start'))

    def test_stopped_storage_is_still_archived_without_being_started(self):
        self.configure(service='inactive')
        result = self.execute()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue((self.destination() / 'object-storage.tar.gz').is_file())
        self.assertEqual('inactive', json.loads(self.state.read_text())['service'])
        self.assertNotIn('systemctl start', self.events.read_text())

    def test_missing_storage_data_cannot_produce_complete(self):
        saved = self.data.with_name('fixture-saved-data')
        self.data.rename(saved)
        self.addCleanup(lambda: saved.rename(self.data))
        self.assert_failure(self.execute())

    def test_transitional_service_cannot_produce_complete(self):
        self.configure(service='activating')
        self.assert_failure(self.execute())
        self.assertNotIn('systemctl stop', self.events.read_text())

    def test_archive_failure_restores_the_original_running_service(self):
        self.configure(tar_failure=True)
        self.assert_failure(self.execute())
        self.assertEqual('active', json.loads(self.state.read_text())['service'])

    def test_restore_failure_cannot_produce_complete(self):
        self.configure(start_failure=True)
        self.assert_failure(self.execute())
        self.assertEqual('inactive', json.loads(self.state.read_text())['service'])

    def test_false_successful_stop_is_detected_before_archiving(self):
        self.configure(false_stop=True)
        self.assert_failure(self.execute())
        self.assertNotIn('object-storage.tar.gz', self.events.read_text())

    def test_primary_keeps_the_dump_snapshot_coordinate_without_a_later_query(self):
        result = self.execute('primary')
        self.assertEqual(0, result.returncode, result.stderr)
        destination = self.destination()
        with gzip.open(destination / 'mysql.sql.gz', 'rt') as stream:
            self.assertIn("SOURCE_LOG_POS=456", stream.read())
        self.assertFalse((destination / 'mysql-binlog-position.tsv').exists())
        self.assertIn('--source-data=2', self.events.read_text())
        self.assertNotIn('SHOW BINARY LOG STATUS', self.events.read_text())
        self.assertEqual('complete', verifier.verify(destination, 'primary')['state'])

    def test_dump_pipeline_failure_does_not_publish_partial_success(self):
        self.configure(dump_failure=True)
        self.assert_failure(self.execute('primary'))

    def test_primary_copies_the_live_sqlite_database_and_keeps_recovery_materials_together(self):
        server = pathlib.Path('/var/lib/rancher/k3s/server')
        for name in ['db', 'tls', 'cred']:
            (server / name).mkdir(parents=True, exist_ok=True)
        database = server / 'db/state.db'
        with sqlite3.connect(database) as connection:
            connection.execute('CREATE TABLE fixture (value TEXT)')
            connection.execute("INSERT INTO fixture VALUES ('original-state')")
        self.addCleanup(database.unlink)
        for name in ['token', 'tls/fixture', 'cred/fixture']:
            (server / name).write_text('fixture')
        for directory in ['/etc/rancher/k3s', '/etc/exports.d', '/etc/nfs.conf.d']:
            pathlib.Path(directory).mkdir(parents=True, exist_ok=True)
        for name in ['/etc/rancher/k3s/config.yaml', '/etc/exports.d/fullnet.exports', '/etc/nfs.conf.d/fullnet.conf']:
            pathlib.Path(name).write_text('fixture')
        result = self.execute('primary')
        self.assertEqual(0, result.returncode, result.stderr)
        destination = self.destination()
        self.assertTrue((destination / 'k3s-recovery.tar.gz').is_file())
        with sqlite3.connect('file:' + str(destination / 'k3s-state.db') + '?mode=ro', uri=True) as restored:
            self.assertEqual('original-state', restored.execute('SELECT value FROM fixture').fetchone()[0])
        self.assertEqual('complete', verifier.verify(destination, 'primary')['state'])

    def test_redis_failure_cannot_produce_complete(self):
        self.configure(redis_failure=True)
        self.assert_failure(self.execute())

    def test_redis_validation_failure_cannot_produce_complete(self):
        self.configure(redis_check_failure=True)
        self.assert_failure(self.execute())

    def test_unknown_role_creates_no_backup_directory(self):
        self.assertNotEqual(0, self.execute('unknown').returncode)
        self.assertEqual(self.before, set(self.backups.iterdir()))

    def test_disk_and_archive_budget_fail_before_creating_a_backup(self):
        for values in [{'low_disk': True}, {'over_budget': True}]:
            with self.subTest(values=values):
                self.configure(**({'low_disk': False, 'over_budget': False} | values))
                self.assertNotEqual(0, self.execute().returncode)
                self.assertEqual(self.before, set(self.backups.iterdir()))

    def test_existing_lock_blocks_the_second_backup(self):
        with open('/run/fullnet-backup.lock', 'a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assertNotEqual(0, self.execute().returncode)
        self.assertEqual(self.before, set(self.backups.iterdir()))

    def test_signal_after_stop_restores_service_and_keeps_backup_incomplete(self):
        self.configure(wait_for_signal=True)
        with subprocess.Popen(['bash', str(self.source)], env=self.environment, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, text=True, start_new_session=True) as process:
            try:
                deadline = time.monotonic() + 10
                while not self.wait.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue(self.wait.exists())
                os.killpg(process.pid, signal.SIGTERM)
                process.communicate(timeout=10)
                self.assertNotEqual(0, process.returncode)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.communicate(timeout=5)
        self.assertEqual('active', json.loads(self.state.read_text())['service'])
        self.assertFalse((self.destination() / 'COMPLETE').exists())


class BackupVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-backup-verifier-')
        self.addCleanup(self.temporary.cleanup)
        self.directory = pathlib.Path(self.temporary.name)
        self.names = {'ROLE', 'redis.rdb', 'redis-check.txt', 'configuration.tar.gz', 'packages.tsv', 'object-storage.tar.gz'}
        for name in self.names:
            (self.directory / name).write_bytes(b'secondary\n' if name == 'ROLE' else b'fixture')
            (self.directory / name).chmod(0o600)
        self.hashes()
        (self.directory / 'COMPLETE').touch(mode=0o600)

    def hashes(self):
        text = ''.join(hashlib.sha256((self.directory / name).read_bytes()).hexdigest() + '  ./' + name + '\n'
                       for name in sorted(self.names))
        (self.directory / 'SHA256SUMS').write_text(text, encoding='ascii')
        (self.directory / 'SHA256SUMS').chmod(0o600)

    def test_complete_bundle_is_integrity_checked_but_not_restore_certified(self):
        self.assertEqual({'schemaVersion': 1, 'role': 'secondary', 'state': 'complete', 'verifiedFiles': 6,
                          'restoreVerified': False}, verifier.verify(self.directory, 'secondary'))

    def test_staging_requires_explicit_mode_and_cannot_claim_complete(self):
        (self.directory / 'COMPLETE').unlink()
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')
        self.assertEqual('staged', verifier.verify(self.directory, 'secondary', staging=True)['state'])

    def test_digest_mismatch_is_rejected(self):
        (self.directory / 'redis.rdb').write_text('changed')
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')

    def test_missing_object_archive_is_rejected_even_if_hashes_are_recomputed(self):
        (self.directory / 'object-storage.tar.gz').unlink()
        self.names.remove('object-storage.tar.gz')
        self.hashes()
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')

    def test_wrong_role_is_rejected(self):
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'primary')

    def test_checksum_path_traversal_or_duplicate_is_rejected(self):
        original = (self.directory / 'SHA256SUMS').read_text()
        for text in [original + original.splitlines()[0] + '\n', original.replace('./redis.rdb', './../redis.rdb')]:
            with self.subTest(kind=text[:12]):
                (self.directory / 'SHA256SUMS').write_text(text)
                with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')

    def test_unknown_or_linked_files_are_rejected(self):
        extra = self.directory / 'unknown'
        extra.symlink_to(self.directory / 'redis.rdb')
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')

    def test_incomplete_checksum_inventory_is_rejected(self):
        text = (self.directory / 'SHA256SUMS').read_text().splitlines()
        (self.directory / 'SHA256SUMS').write_text('\n'.join(text[:-1]) + '\n')
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')

    def test_world_readable_payload_or_directory_is_rejected(self):
        for target in [self.directory, self.directory / 'configuration.tar.gz']:
            with self.subTest(target=target.name):
                original = target.stat().st_mode & 0o777
                target.chmod(0o755 if target.is_dir() else 0o644)
                with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'secondary')
                target.chmod(original)

    def test_restore_markers_do_not_become_a_restore_claim(self):
        (self.directory / 'RESTORE-VERIFIED').write_text('untrusted marker')
        (self.directory / 'OBJECT-RESTORE-VERIFIED').write_text('untrusted marker')
        self.assertFalse(verifier.verify(self.directory, 'secondary')['restoreVerified'])

    def test_primary_k3s_pair_cannot_be_partially_present(self):
        (self.directory / 'object-storage.tar.gz').unlink()
        self.names.remove('object-storage.tar.gz')
        self.names |= {'mysql.sql.gz', 'mysql-configuration.tar.gz', 'k3s-state.db'}
        for name in self.names:
            (self.directory / name).write_text('primary\n' if name == 'ROLE' else 'fixture')
            (self.directory / name).chmod(0o600)
        self.hashes()
        with self.assertRaises(verifier.InvalidBackup): verifier.verify(self.directory, 'primary')

    def test_cli_redacts_filesystem_errors(self):
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            self.assertEqual(1, verifier.main([str(self.directory / 'missing'), '--role', 'secondary']))
        self.assertNotIn(str(self.directory), stdout.getvalue() + stderr.getvalue())


if __name__ == '__main__':
    unittest.main()
