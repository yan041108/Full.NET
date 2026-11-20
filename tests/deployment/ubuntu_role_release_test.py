"""在一次性容器内验证角色发布与 DDL 退出边界，真实 Helm 仅渲染，不连接集群。"""
import fcntl
import json
import os
import pathlib
import re
import signal
import subprocess
import tempfile
import time
import unittest


REPOSITORY = pathlib.Path(__file__).resolve().parents[2]
PREPARED = False
STUB = r'''#!/usr/bin/env python3
import hashlib,json,os,pathlib,re,subprocess,sys,time
statefile=pathlib.Path(os.environ['FULLNET_RELEASE_TEST_STATE'])
state=json.loads(statefile.read_text())
name=pathlib.Path(sys.argv[0]).name
args=sys.argv[1:]
state['events'].append([name]+args)
def save(): statefile.write_text(json.dumps(state))
def fail(): print('secret-sentinel backend details',file=sys.stderr);save();sys.exit(1)
if name=='systemctl':
    if args[0]=='start' and state.get('backup_failure'): fail()
    if args[0]=='show': print(state.get('backup_result','success'))
elif name=='mysql':
    if 'SELECT' in args[-1]: print(state.get('mysql_value',0))
    else:
        value=int(re.search(r'=([01]);',args[-1])[1])
        if value==0 and state.get('restore_failure'): fail()
        if not (value==0 and state.get('restore_no_change')) and not (value==1 and state.get('enable_no_change')):
            state['mysql_value']=value
        if value==1 and state.get('enable_response_lost'): fail()
elif name=='helm':
    operation=args[0]
    role=args[1].removeprefix('fullnet-') if operation=='template' else args[2].removeprefix('fullnet-') if operation=='upgrade' else None
    if state.get('real_helm'):
        result=subprocess.run([os.environ['FULLNET_TEST_REAL_HELM']]+args,capture_output=True,text=True)
        if operation=='template':
            rendered=pathlib.Path(os.environ['FULLNET_RELEASE_TEST_RENDER'])
            rendered.write_text(result.stdout)
            rendered.with_name(rendered.stem+'-'+role+'.yaml').write_text(result.stdout)
        sys.stdout.write(result.stdout);sys.stderr.write(result.stderr);save();sys.exit(result.returncode)
    if state.get('fail_phase')==operation and (not state.get('fail_role') or state['fail_role']==role): fail()
    if operation=='package':
        target=pathlib.Path(args[args.index('--destination')+1])/'fullnet-fixture.tgz'
        target.write_bytes(b'frozen-chart-fixture')
    if operation in ('template','upgrade'):
        package=pathlib.Path(args[2] if operation=='template' else args[3])
        values=[pathlib.Path(args[index+1]) for index,value in enumerate(args) if value=='-f']
        state[operation+'_values']=[file.read_text() for file in values]
        state[operation+'_package']=hashlib.sha256(package.read_bytes()).hexdigest()
        state.setdefault(operation+'_records',[]).append({'role':role,'values':state[operation+'_values'],
            'package':state[operation+'_package'],'mysql_value':state.get('mysql_value',0)})
        if operation=='template' and state.get('mutate_original'):
            pathlib.Path('/etc/fullnet/release/common.json').write_text('changed-after-render')
            for member in ('migrator','worker','api'):
                pathlib.Path('/etc/fullnet/release/'+member+'.json').write_text('changed-original-'+member)
        if operation=='template' and state.get('tamper_frozen') and role==state.get('tamper_after_role','migrator'):
            (values[0].parent/(state['tamper_frozen']+'.json')).write_text('changed-frozen-input')
        if operation=='upgrade' and state.get('wait_for_signal'):
            pathlib.Path(os.environ['FULLNET_RELEASE_TEST_WAIT']).touch();save();time.sleep(30)
save()
'''


class _Base(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        global PREPARED
        if os.geteuid() != 0 or not pathlib.Path('/.dockerenv').exists() or os.environ.get('FULLNET_RELEASE_TEST_CONTAINER') != 'isolated':
            raise RuntimeError('发布测试只允许在专用隔离容器执行')
        if not PREPARED:
            if pathlib.Path('/etc/fullnet').exists():
                raise RuntimeError('拒绝接管容器已有配置')
            pathlib.Path('/etc/fullnet/release').mkdir(parents=True, mode=0o700)
            PREPARED = True

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='fullnet-release-fixture-')
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.statefile, self.wait, self.render = (self.root / name for name in ('state.json', 'waiting', 'rendered.yaml'))
        self.statefile.write_text(json.dumps({'mysql_value': 0, 'events': []}))
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        for name in ['helm', 'mysql', 'systemctl']:
            file = self.bin / name
            file.write_text(STUB, encoding='utf-8')
            file.chmod(0o700)
        self.environment = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ['PATH'],
            FULLNET_RELEASE_TEST_STATE=str(self.statefile), FULLNET_RELEASE_TEST_WAIT=str(self.wait),
            FULLNET_RELEASE_TEST_RENDER=str(self.render), PYTHONOPTIMIZE='1')
        self.chart = self.root / 'chart'
        self.chart.mkdir()
        (self.chart / 'Chart.yaml').write_text('apiVersion: v2\nname: fullnet\nversion: 1.0.0\n')
        self.common = pathlib.Path('/etc/fullnet/release/common.json')
        self.common.write_text(json.dumps({'fixture': 'secret-sentinel'}))
        for role in ('api', 'worker', 'migrator'):
            pathlib.Path('/etc/fullnet/release/' + role + '.json').write_text(json.dumps({'roles': {'api': True, 'worker': True, 'migrator': True}}))
        self.before = set(pathlib.Path('/run').glob('fullnet-release-*'))

    def state(self):
        return json.loads(self.statefile.read_text())

    def configure(self, **values):
        state = self.state()
        state.update(values)
        self.statefile.write_text(json.dumps(state))

    def execute(self, role='migrator', *extra):
        result = subprocess.run(['bash', str(REPOSITORY / 'eng/deploy/ubuntu/Release-Application.sh'), str(self.chart), role, *extra],
            env=self.environment, capture_output=True, text=True, timeout=25)
        self.assertNotIn('secret-sentinel', result.stdout + result.stderr)
        return result

    def assert_no_success(self, result):
        self.assertNotEqual(0, result.returncode)
        self.assertEqual('', result.stdout)


class ReleaseControlTests(_Base):
    def test_plan_renders_without_backup_mysql_or_upgrade(self):
        result = self.execute('migrator', '--plan')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('plan', json.loads(result.stdout)['action'])
        events = self.state()['events']
        self.assertFalse(any(event[0] in ('mysql', 'systemctl') or event[1] == 'upgrade' for event in events))
        self.assertEqual(self.before, set(pathlib.Path('/run').glob('fullnet-release-*')))

    def test_migrator_restores_zero_before_publishing_success(self):
        result = self.execute()
        self.assertEqual(0, result.returncode, result.stderr)
        state = self.state()
        self.assertEqual(0, state['mysql_value'])
        self.assertEqual('applied', json.loads(result.stdout)['action'])
        self.assertEqual(state['template_package'], state['upgrade_package'])
        self.assertEqual(state['template_values'], state['upgrade_values'])

    def test_original_one_is_preserved(self):
        self.configure(mysql_value=1)
        self.assertEqual(0, self.execute().returncode)
        self.assertEqual(1, self.state()['mysql_value'])

    def test_unknown_role_or_ignored_dry_run_flag_has_no_side_effects(self):
        for role, extra in [('unknown', ()), ('api', ('--dry-run',))]:
            with self.subTest(role=role):
                self.assert_no_success(self.execute(role, *extra))
                self.assertEqual([], self.state()['events'])

    def test_package_or_render_failure_does_not_start_backup_or_ddl(self):
        for phase in ('package', 'template'):
            with self.subTest(phase=phase):
                self.configure(fail_phase=phase, events=[])
                self.assert_no_success(self.execute())
                self.assertFalse(any(event[0] in ('mysql', 'systemctl') for event in self.state()['events']))

    def test_backup_failure_blocks_ddl_and_upgrade(self):
        self.configure(backup_failure=True)
        self.assert_no_success(self.execute())
        self.assertFalse(any(event[0] == 'mysql' or event[1] == 'upgrade' for event in self.state()['events']))

    def test_failed_backup_result_blocks_ddl(self):
        self.configure(backup_result='failed')
        self.assert_no_success(self.execute())
        self.assertFalse(any(event[0] == 'mysql' for event in self.state()['events']))

    def test_invalid_original_value_is_not_interpolated_into_sql(self):
        self.configure(mysql_value='invalid')
        self.assert_no_success(self.execute())
        self.assertFalse(any('SET GLOBAL' in event[-1] for event in self.state()['events']))

    def test_enable_response_loss_still_restores_original(self):
        self.configure(enable_response_lost=True)
        self.assert_no_success(self.execute())
        self.assertEqual(0, self.state()['mysql_value'])
        self.assertFalse(any(event[1] == 'upgrade' for event in self.state()['events']))

    def test_silent_enable_failure_is_detected(self):
        self.configure(enable_no_change=True)
        self.assert_no_success(self.execute())
        self.assertEqual(0, self.state()['mysql_value'])
        self.assertFalse(any(event[1] == 'upgrade' for event in self.state()['events']))

    def test_upgrade_failure_restores_original(self):
        self.configure(fail_phase='upgrade')
        self.assert_no_success(self.execute())
        self.assertEqual(0, self.state()['mysql_value'])

    def test_failed_or_silent_restore_does_not_report_success(self):
        for values in ({'restore_failure': True}, {'restore_no_change': True}):
            with self.subTest(values=values):
                self.configure(mysql_value=0, restore_failure=False, restore_no_change=False)
                self.configure(**values)
                self.assert_no_success(self.execute())
                self.assertEqual(1, self.state()['mysql_value'])

    def test_changed_original_values_do_not_change_the_frozen_upgrade(self):
        self.configure(mutate_original=True)
        self.assertEqual(0, self.execute().returncode)
        self.assertEqual('changed-after-render', self.common.read_text())
        self.assertEqual(self.state()['template_values'], self.state()['upgrade_values'])

    def test_worker_and_api_do_not_open_a_ddl_window(self):
        for role in ('worker', 'api'):
            with self.subTest(role=role):
                self.configure(events=[])
                self.assertEqual(0, self.execute(role).returncode)
                self.assertFalse(any(event[0] in ('mysql', 'systemctl') for event in self.state()['events']))

    def test_failed_release_retains_only_private_bounded_diagnostics(self):
        self.configure(fail_phase='upgrade')
        self.assert_no_success(self.execute())
        created = set(pathlib.Path('/run').glob('fullnet-release-*')) - self.before
        self.assertEqual(1, len(created))
        directory = created.pop()
        self.assertEqual(0, directory.stat().st_mode & 0o077)
        files = list(directory.iterdir())
        self.assertTrue(files)
        self.assertTrue(all(file.suffix == '.log' and file.stat().st_size <= 65536 and file.stat().st_mode & 0o077 == 0 for file in files))

    def test_active_lock_blocks_the_second_release(self):
        with open('/run/fullnet-release.lock', 'a') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            self.assert_no_success(self.execute())
        self.assertEqual([], self.state()['events'])

    def test_api_gateway_failure_does_not_publish_success(self):
        nginx = pathlib.Path('/etc/nginx/conf.d/fullnet.conf')
        nginx.parent.mkdir(parents=True, exist_ok=True)
        nginx.write_text('fixture')
        self.addCleanup(nginx.unlink)
        gateway = pathlib.Path('/usr/local/lib/fullnet/Refresh-Edge.py')
        gateway.parent.mkdir(parents=True, exist_ok=True)
        gateway.write_text("import sys; print('secret-sentinel', file=sys.stderr); raise SystemExit(1)")
        self.assert_no_success(self.execute('api'))
        self.assertFalse(any(event[0] == 'mysql' for event in self.state()['events']))

    def test_signal_during_upgrade_restores_original_without_success(self):
        self.configure(wait_for_signal=True)
        args = ['bash', str(REPOSITORY / 'eng/deploy/ubuntu/Release-Application.sh'), str(self.chart), 'migrator']
        with subprocess.Popen(args, env=self.environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              text=True, start_new_session=True) as process:
            try:
                deadline = time.monotonic() + 10
                while not self.wait.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue(self.wait.exists())
                os.killpg(process.pid, signal.SIGTERM)
                stdout, stderr = process.communicate(timeout=10)
                self.assertNotEqual(0, process.returncode)
                self.assertEqual('', stdout)
                self.assertNotIn('secret-sentinel', stderr)
            finally:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.communicate(timeout=5)
        self.assertEqual(0, self.state()['mysql_value'])


class RealHelmRolePlanTests(_Base):
    def test_actual_chart_renders_exactly_the_selected_role_despite_all_roles_in_values(self):
        self.chart = REPOSITORY / 'deploy/helm/fullnet'
        common = (self.chart / 'ci/values-role-api.yaml').read_text() + '\n' + (self.chart / 'ci/values-provider-sqlserver.yaml').read_text()
        self.common.write_text(common)
        for role in ('api', 'worker', 'migrator'):
            with self.subTest(role=role):
                self.configure(real_helm=True, events=[])
                result = self.execute(role, '--plan')
                self.assertEqual(0, result.returncode, result.stderr)
                manifest = self.render.read_text()
                self.assertEqual(1 if role in ('api', 'worker') else 0, len(re.findall(r'^kind: Deployment$', manifest, re.M)))
                self.assertEqual(1 if role == 'migrator' else 0, len(re.findall(r'^kind: Job$', manifest, re.M)))
                self.assertIn('app.kubernetes.io/component: ' + role, manifest)
                self.assertEqual(role, json.loads(result.stdout)['role'])
                self.assertFalse(any(event[0] in ('mysql', 'systemctl') for event in self.state()['events']))

    def test_batch_actual_chart_renders_each_role_once_with_one_package(self):
        self.chart = REPOSITORY / 'deploy/helm/fullnet'
        self.common.write_text((self.chart / 'ci/values-role-api.yaml').read_text() + '\n'
                               + (self.chart / 'ci/values-provider-sqlserver.yaml').read_text())
        self.configure(real_helm=True)
        result = self.execute('all', '--plan')
        self.assertEqual(result.returncode, 0, result.stderr)
        receipt = json.loads(result.stdout)
        self.assertEqual(receipt['roles'], ['migrator', 'worker', 'api'])
        self.assertEqual(receipt['completedRoles'], [])
        self.assertEqual(set(receipt['valuesSha256']), {'common', 'migrator', 'worker', 'api'})
        for role in ('migrator', 'worker', 'api'):
            manifest = self.render.with_name(self.render.stem + '-' + role + '.yaml').read_text()
            self.assertEqual(1 if role in ('api', 'worker') else 0, len(re.findall(r'^kind: Deployment$', manifest, re.M)))
            self.assertEqual(1 if role == 'migrator' else 0, len(re.findall(r'^kind: Job$', manifest, re.M)))
            self.assertIn('app.kubernetes.io/component: ' + role, manifest)
        events = self.state()['events']
        self.assertEqual(len([event for event in events if event[:2] == ['helm', 'package']]), 1)
        self.assertFalse(any(event[0] in ('mysql', 'systemctl') or event[1] == 'upgrade' for event in events))


if __name__ == '__main__':
    unittest.main()
