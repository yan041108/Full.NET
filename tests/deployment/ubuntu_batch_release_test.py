"""批量角色的集中冻结、顺序和失败退出验收；复用既有隔离命令夹具。"""
import hashlib
import json
import pathlib
import unittest

from ubuntu_role_release_test import _Base

ROLES = ['migrator', 'worker', 'api']


class BatchReleaseTests(_Base):
    def setUp(self):
        super().setUp()
        self.common.write_text(json.dumps({'fixture': 'secret-sentinel', 'image': {'tag': '1.0.0-fixture'}}))
        for role in ROLES:
            pathlib.Path('/etc/fullnet/release/' + role + '.json').write_text(json.dumps({'selectedRole': role}))

    def batch(self, plan=False):
        return self.execute('all', '--plan' if plan else '--ordinary-upgrade')

    def test_missing_or_latest_tag_blocks_every_release_command(self):
        for image in ({}, {'tag': 'latest'}, {'tag': ''}, {'tag': True}):
            self.common.write_text(json.dumps({'image': image}))
            result = self.batch(plan=True)
            self.assert_no_success(result)
            self.assertEqual(self.state()['events'], [])

    def test_role_image_override_cannot_mix_candidates(self):
        for image in ({'tag': 'different'}, {'registry': 'other.internal'}, {'repository': 'other'}):
            pathlib.Path('/etc/fullnet/release/worker.json').write_text(json.dumps({'image': image}))
            self.assert_no_success(self.batch())
            self.assertEqual(self.state()['events'], [])

    def test_invalid_json_shape_and_unbounded_values_are_rejected(self):
        for content in ('image:\n  tag: legacy-yaml', '[]', '{"image":null}', ' ' * 1048577):
            self.common.write_text(content)
            self.assert_no_success(self.batch(plan=True))
            self.assertEqual(self.state()['events'], [])

    def test_identical_explicit_role_overrides_are_allowed(self):
        for role in ROLES:
            pathlib.Path('/etc/fullnet/release/' + role + '.json').write_text(json.dumps({'image': {'tag': 'same-candidate'}}))
        result = self.batch(plan=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['candidateImageTag'], 'same-candidate')

    def test_ambiguous_json_and_nonstandard_numbers_block_before_commands(self):
        for content in ('{"image":{"tag":"first","tag":"second"}}',
                '{"image":{"tag":"first"},"image":{"tag":"second"}}',
                '{"image":{"tag":"valid"},"other":NaN}',
                '{"image":{"tag":"valid"},"other":Infinity}'):
            self.common.write_text(content)
            self.assert_no_success(self.batch(plan=True))
            self.assertEqual(self.state()['events'], [])

    def failure_receipt(self, result):
        self.assert_no_success(result)
        return json.loads(next(line for line in result.stderr.splitlines() if line.startswith('{')))

    def test_batch_requires_explicit_mode_before_commands(self):
        self.assert_no_success(self.execute('all'))
        self.assertEqual(self.state()['events'], [])
        self.assert_no_success(self.execute('api', '--ordinary-upgrade'))
        self.assertEqual(self.state()['events'], [])

    def test_plan_freezes_once_and_renders_all_without_mutation(self):
        result = self.batch(plan=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        receipt = json.loads(result.stdout)
        self.assertEqual(receipt['roles'], ROLES)
        self.assertEqual(receipt['completedRoles'], [])
        self.assertEqual(receipt['action'], 'plan')
        self.assertEqual(receipt['candidateImageTag'], '1.0.0-fixture')
        state = self.state()
        self.assertEqual([record['role'] for record in state['template_records']], ROLES)
        self.assertEqual(len(set(record['package'] for record in state['template_records'])), 1)
        self.assertEqual(len([event for event in state['events'] if event[:2] == ['helm', 'package']]), 1)
        self.assertFalse(any(event[0] in ('mysql', 'systemctl') or event[1] == 'upgrade' for event in state['events']))
        self.assertEqual(self.before, set(pathlib.Path('/run').glob('fullnet-release-*')))

    def test_batch_applies_in_order_after_all_rendering_and_restores_ddl_before_worker(self):
        result = self.batch()
        self.assertEqual(result.returncode, 0, result.stderr)
        receipt = json.loads(result.stdout)
        self.assertEqual(receipt['completedRoles'], ROLES)
        self.assertEqual(receipt['candidateImageTag'], '1.0.0-fixture')
        state = self.state()
        self.assertEqual([record['role'] for record in state['upgrade_records']], ROLES)
        self.assertEqual([record['mysql_value'] for record in state['upgrade_records']], [1, 0, 0])
        self.assertEqual(state['mysql_value'], 0)
        events = state['events']
        last_template = max(index for index, event in enumerate(events) if event[:2] == ['helm', 'template'])
        backup = next(index for index, event in enumerate(events) if event[:3] == ['systemctl', 'start', 'fullnet-backup.service'])
        self.assertLess(last_template, backup)
        self.assertEqual(len([event for event in events if event[:3] == ['systemctl', 'start', 'fullnet-backup.service']]), 1)
        self.assertEqual(self.before, set(pathlib.Path('/run').glob('fullnet-release-*')))

    def test_each_role_uses_exact_frozen_values_and_common_package(self):
        result = self.batch()
        self.assertEqual(result.returncode, 0, result.stderr)
        state = self.state()
        for planned, applied in zip(state['template_records'], state['upgrade_records'], strict=True):
            self.assertEqual(planned['role'], applied['role'])
            self.assertEqual(planned['values'], applied['values'])
            self.assertEqual(planned['package'], applied['package'])
            self.assertEqual(json.loads(applied['values'][1])['selectedRole'], applied['role'])

    def test_source_values_changed_after_render_do_not_change_any_upgrade(self):
        self.configure(mutate_original=True)
        result = self.batch()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.common.read_text(), 'changed-after-render')
        self.assertEqual(json.loads(result.stdout)['candidateImageTag'], '1.0.0-fixture')
        state = self.state()
        self.assertEqual([record['values'] for record in state['template_records']],
                         [record['values'] for record in state['upgrade_records']])
        for role in ROLES:
            self.assertEqual(pathlib.Path('/etc/fullnet/release/' + role + '.json').read_text(), 'changed-original-' + role)

    def test_plan_receipt_binds_all_four_values_hashes(self):
        expected = {'common': hashlib.sha256(self.common.read_bytes()).hexdigest()}
        expected.update({role: hashlib.sha256(pathlib.Path('/etc/fullnet/release/' + role + '.json').read_bytes()).hexdigest() for role in ROLES})
        result = self.batch(plan=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['valuesSha256'], expected)

    def test_late_render_failure_prevents_backup_ddl_and_all_upgrades(self):
        for role in ('worker', 'api'):
            with self.subTest(role=role):
                self.configure(fail_phase='template', fail_role=role, events=[])
                receipt = self.failure_receipt(self.batch())
                self.assertEqual(receipt['completedRoles'], [])
                self.assertFalse(any(event[0] in ('mysql', 'systemctl') or event[1] == 'upgrade' for event in self.state()['events']))

    def test_frozen_values_tampering_during_render_prevents_all_modifications(self):
        for role in ('migrator', 'api'):
            with self.subTest(after_role=role):
                self.configure(tamper_frozen='api', tamper_after_role=role, events=[])
                self.failure_receipt(self.batch())
                self.assertFalse(any(event[0] in ('mysql', 'systemctl') or event[1] == 'upgrade' for event in self.state()['events']))

    def test_final_render_tampering_also_prevents_plan_success(self):
        self.configure(tamper_frozen='common', tamper_after_role='api')
        self.failure_receipt(self.batch(plan=True))

    def test_missing_later_role_values_prevents_any_helm_or_service_call(self):
        pathlib.Path('/etc/fullnet/release/api.json').unlink()
        receipt = self.failure_receipt(self.batch())
        self.assertEqual(receipt['completedRoles'], [])
        self.assertEqual(self.state()['events'], [])

    def test_migrator_failure_stops_later_roles_and_restores_ddl(self):
        self.configure(fail_phase='upgrade', fail_role='migrator')
        receipt = self.failure_receipt(self.batch())
        self.assertEqual(receipt['completedRoles'], [])
        self.assertFalse(receipt['ddlRestorePending'])
        self.assertEqual(self.state()['mysql_value'], 0)
        self.assertEqual([event[3] for event in self.state()['events'] if event[:2] == ['helm', 'upgrade']], ['fullnet-migrator'])

    def test_worker_failure_reports_only_migrator_completed(self):
        self.configure(fail_phase='upgrade', fail_role='worker')
        receipt = self.failure_receipt(self.batch())
        self.assertEqual(receipt['completedRoles'], ['migrator'])
        upgrades = [event for event in self.state()['events'] if event[:2] == ['helm', 'upgrade']]
        self.assertEqual([event[3] for event in upgrades], ['fullnet-migrator', 'fullnet-worker'])
        self.assertEqual(self.state()['mysql_value'], 0)

    def test_api_failure_reports_completed_migrator_and_worker(self):
        self.configure(fail_phase='upgrade', fail_role='api')
        receipt = self.failure_receipt(self.batch())
        self.assertEqual(receipt['completedRoles'], ['migrator', 'worker'])
        self.assertFalse(receipt['ddlRestorePending'])

    def test_ddl_restore_failure_blocks_worker_and_reports_pending_restore(self):
        self.configure(restore_failure=True)
        receipt = self.failure_receipt(self.batch())
        self.assertEqual(receipt['completedRoles'], [])
        self.assertTrue(receipt['ddlRestorePending'])
        upgrades = [event for event in self.state()['events'] if event[:2] == ['helm', 'upgrade']]
        self.assertEqual([event[3] for event in upgrades], ['fullnet-migrator'])

    def test_original_enabled_ddl_setting_is_preserved_for_whole_batch(self):
        self.configure(mysql_value=1)
        result = self.batch()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual([record['mysql_value'] for record in self.state()['upgrade_records']], [1, 1, 1])
        self.assertEqual(self.state()['mysql_value'], 1)


if __name__ == '__main__':
    unittest.main()
