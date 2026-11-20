"""真实临时文件系统与 -O 准入回归；修改调用均截断，不操作集群或系统服务。"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / 'eng/deploy/ubuntu/Configure-InternalEdge.py'
spec = importlib.util.spec_from_file_location('gateway_configuration', SCRIPT)
EDGE = importlib.util.module_from_spec(spec)
spec.loader.exec_module(EDGE)

# 子进程真正开启优化模式；错误地越过准入也只能记录哨兵，不能触及系统命令。
HARNESS = r'''
import importlib.util,json,os,pathlib,sys
spec=importlib.util.spec_from_file_location('edge',sys.argv[1]);edge=importlib.util.module_from_spec(spec);spec.loader.exec_module(edge)
for key,value in json.loads(os.environ['EDGE_TEST_PATHS']).items():setattr(edge,key,pathlib.Path(value))
def stopped(*args,**kwargs):
 pathlib.Path(os.environ['EDGE_MUTATION_MARKER']).write_text('unexpected-mutation')
 raise OSError('controlled-stop')
edge.apply=stopped;edge.run=stopped
sys.exit(edge.main(sys.argv[2:]))
'''


class GatewayConfigurationAdmissionTests(unittest.TestCase):
    def setUp(self):
        self.assertEqual(os.geteuid(), 0, '文件所有权和降权子进程验收必须在本轮隔离容器内实际执行。')
        self.temporary = tempfile.TemporaryDirectory(prefix='fullnet-edge-admission-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        installation = self.root / 'installation'
        releases = installation / 'releases'
        self.release = releases / 'v1'
        frontend = self.release / 'frontend'
        frontend.mkdir(parents=True, mode=0o700)
        self.index = frontend / 'index.html'
        self.index.write_text('<html>synthetic fixture</html>')
        configuration = self.root / 'nginx' / 'conf.d' / 'fullnet.conf'
        configuration.parent.mkdir(parents=True)
        certificates = self.root / 'certificates'
        certificates.mkdir()
        certificate, key = certificates / 'web.pem', certificates / 'web.key'
        certificate.write_text('synthetic-public-certificate')
        key.write_text('synthetic-key-no-real-credential')
        key.chmod(0o600)
        self.paths = {'RELEASES': releases, 'CURRENT': installation / 'current-web',
            'PENDING': installation / '.next-web', 'CONFIGURATION': configuration,
            'WEB_CERTIFICATE': certificate, 'WEB_KEY': key}
        self.enterContext(patch.multiple(EDGE, **self.paths))
        self.arguments = ['192.168.1.10', '192.168.1.11', '192.168.1.20', str(self.release)]
        self.marker = self.root / 'mutation-marker'

    def main(self, arguments=None):
        output, errors = io.StringIO(), io.StringIO()
        with (patch.object(EDGE, 'configure') as configure, patch.object(EDGE, 'apply') as apply,
              patch.object(EDGE, 'run') as run, contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors)):
            status = EDGE.main(arguments if arguments is not None else ['--check', *self.arguments])
        apply.assert_not_called()
        run.assert_not_called()
        return status, output.getvalue(), errors.getvalue(), configure

    def failed(self, code, arguments=None):
        status, output, errors, configure = self.main(arguments)
        self.assertEqual(status, 1)
        self.assertEqual(output, '')
        self.assertEqual(json.loads(errors), {'status': 'failed', 'code': code, 'modificationsMayHaveOccurred': False})
        configure.assert_not_called()

    def cli(self, arguments, unprivileged=False):
        return subprocess.run([sys.executable, '-O', '-c', HARNESS, str(SCRIPT), *arguments],
            env={**os.environ, 'EDGE_TEST_PATHS': json.dumps({key: str(value) for key, value in self.paths.items()}),
                 'EDGE_MUTATION_MARKER': str(self.marker)},
            preexec_fn=(lambda: os.setuid(65534)) if unprivileged else None,
            capture_output=True, text=True, timeout=5)

    def test_check_success_is_read_only_and_does_not_claim_activation(self):
        before = {str(path): (path.read_bytes(), path.stat().st_ino, path.stat().st_mtime_ns)
                  for path in self.root.rglob('*') if path.is_file()}
        status, output, errors, configure = self.main()
        self.assertEqual(status, 0, errors)
        self.assertEqual(json.loads(output), {'status': 'admitted', 'changesApplied': False, 'runtimeActivationVerified': False})
        configure.assert_not_called()
        after = {str(path): (path.read_bytes(), path.stat().st_ino, path.stat().st_mtime_ns)
                 for path in self.root.rglob('*') if path.is_file()}
        self.assertEqual(before, after)
        self.assertFalse(self.paths['CURRENT'].exists())

    def test_normal_execution_calls_configuration_only_after_admission(self):
        status, _, errors, configure = self.main(self.arguments)
        self.assertEqual(status, 0, errors)
        configure.assert_called_once_with(*self.arguments[:3], self.release)

    def test_actual_non_root_optimized_cli_rejects_before_any_modification(self):
        result = self.cli(self.arguments, unprivileged=True)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(json.loads(result.stderr)['code'], 'root_required')
        self.assertFalse(self.marker.exists())

    def test_outside_release_optimized_cli_never_reaches_modification(self):
        outside = self.root / 'outside'
        outside.mkdir()
        result = self.cli([*self.arguments[:3], str(outside)])
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(json.loads(result.stderr)['code'], 'release_outside_releases')
        self.assertFalse(self.marker.exists())

    def test_regular_current_pointer_is_rejected_before_first_external_call(self):
        self.paths['CURRENT'].write_bytes(b'unrelated-owner-content')
        result = self.cli(self.arguments)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(json.loads(result.stderr)['code'], 'frontend_pointer_invalid')
        self.assertEqual(self.paths['CURRENT'].read_bytes(), b'unrelated-owner-content')
        self.assertFalse(self.marker.exists())

    def test_pending_regular_file_and_dangling_symlink_are_both_preserved(self):
        pending = self.paths['PENDING']
        pending.write_bytes(b'unrelated-pending')
        self.failed('frontend_pending_conflict')
        self.assertEqual(pending.read_bytes(), b'unrelated-pending')
        pending.unlink()
        pending.symlink_to(self.root / 'missing')
        self.failed('frontend_pending_conflict')
        self.assertTrue(pending.is_symlink())

    def test_dangling_current_pointer_within_release_root_is_allowed(self):
        self.paths['CURRENT'].symlink_to(self.paths['RELEASES'] / 'previous' / 'frontend')
        status, _, errors, configure = self.main()
        self.assertEqual(status, 0, errors)
        configure.assert_not_called()
        self.assertTrue(self.paths['CURRENT'].is_symlink())

    def test_current_pointer_outside_release_root_is_rejected(self):
        self.paths['CURRENT'].symlink_to(self.root / 'outside')
        self.failed('frontend_pointer_outside_releases')

    def test_missing_frontend_index_is_rejected(self):
        self.index.unlink()
        self.failed('gateway_configuration_failed')

    def test_linked_frontend_index_is_rejected(self):
        self.index.unlink()
        target = self.root / 'untouched-index'
        target.write_text('unrelated')
        self.index.symlink_to(target)
        self.failed('deployment_path_permissions_invalid')
        self.assertEqual(target.read_text(), 'unrelated')

    def test_frontend_directory_symlink_is_rejected(self):
        frontend = self.index.parent
        frontend.rename(self.release / 'real-frontend')
        frontend.symlink_to(self.release / 'real-frontend', target_is_directory=True)
        self.failed('deployment_path_permissions_invalid')

    def test_writable_release_ancestor_is_rejected(self):
        self.release.chmod(0o777)
        self.failed('deployment_path_permissions_invalid')

    def test_same_infrastructure_node_is_rejected(self):
        self.failed('infrastructure_nodes_not_distinct', ['--check', self.arguments[0], self.arguments[0], *self.arguments[2:]])

    def test_special_ipv4_addresses_are_rejected_before_modification(self):
        for value in ('0.0.0.0', '127.0.0.1', '169.254.1.1', '224.0.0.1', '255.255.255.255'):
            with self.subTest(address=value):
                self.failed('gateway_address_invalid', ['--check', value, *self.arguments[1:]])

    def test_configuration_symlink_preserves_target(self):
        target = self.root / 'other-configuration'
        target.write_text('unrelated')
        self.paths['CONFIGURATION'].symlink_to(target)
        self.failed('deployment_path_permissions_invalid')
        self.assertEqual(target.read_text(), 'unrelated')

    def test_world_readable_private_key_is_rejected(self):
        self.paths['WEB_KEY'].chmod(0o644)
        self.failed('web_key_permissions_invalid')

    def test_missing_or_empty_certificate_is_rejected(self):
        self.paths['WEB_CERTIFICATE'].write_bytes(b'')
        self.failed('deployment_file_invalid')
        self.paths['WEB_CERTIFICATE'].unlink()
        self.failed('gateway_configuration_failed')

    def test_render_is_read_only_and_needs_no_root(self):
        with patch.object(EDGE.os, 'geteuid', return_value=1000):
            status, output, errors, configure = self.main(['--render', self.arguments[0]])
        self.assertEqual(status, 0, errors)
        self.assertIn('listen 192.168.1.10:443 ssl', output)
        self.assertIn('proxy_bind 192.168.1.10;', output)
        configure.assert_not_called()

    def test_error_after_admission_marks_possible_changes_and_redacts_response(self):
        output, errors = io.StringIO(), io.StringIO()
        with (patch.object(EDGE, 'configure', side_effect=subprocess.CalledProcessError(1, 'private-command', stderr='private-response')),
              contextlib.redirect_stdout(output), contextlib.redirect_stderr(errors)):
            status = EDGE.main(self.arguments)
        self.assertEqual(status, 1)
        self.assertEqual(output.getvalue(), '')
        self.assertTrue(json.loads(errors.getvalue())['modificationsMayHaveOccurred'])
        self.assertNotIn('private', errors.getvalue())

    def test_hardlinked_frontend_index_is_rejected(self):
        os.link(self.index, self.root / 'other-index')
        self.failed('deployment_file_invalid')


if __name__ == '__main__':
    unittest.main()