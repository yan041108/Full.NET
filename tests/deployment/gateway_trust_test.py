"""精确代理信任的条件写入回归；所有资源均为合成数据，不使用真实凭据。"""
import contextlib
import copy
import importlib.util
import io
import json
import subprocess
from pathlib import Path
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[2] / 'eng/deploy/ubuntu/Configure-GatewayTrust.py'
spec = importlib.util.spec_from_file_location('gateway_trust', SCRIPT)
TRUST = importlib.util.module_from_spec(spec)
spec.loader.exec_module(TRUST)
PRIMARY = '192.168.1.10'


def resource(kind, name, **fields):
    return {'kind': kind, 'metadata': {'name': name, 'namespace': TRUST.NAMESPACE,
            'uid': kind + '-uid', 'resourceVersion': '1', 'annotations': {'unrelated': 'retained'}}, **fields}


class FakeClient:
    def __init__(self):
        self.nodes = [{'metadata': {'uid': 'node-uid', 'name': 'gateway'},
            'status': {'addresses': [{'type': 'InternalIP', 'address': PRIMARY}],
                       'conditions': [{'type': 'Ready', 'status': 'True'}]}, 'spec': {'podCIDR': '10.42.0.0/24'}}]
        self.objects = {('ConfigMap', TRUST.CONFIGURATION): resource('ConfigMap', TRUST.CONFIGURATION, data={
            'Other': 'retained', 'TrustedProxy__KnownProxies__2': '10.42.99.99',
            'trustedproxy__knownnetworks__0': '10.42.0.0/16', 'TrustedProxy__ForwardLimit': '9'})}
        self.writes, self.node_reads = [], 0
        self.before_write = None
        self.after_write = None
        self.on_get = None
        self.on_nodes = None
        self.lose_response = None
        self.reject_write = None
        self.get_counts = {}

    def get(self, kind, name):
        key = (kind, name)
        self.get_counts[key] = self.get_counts.get(key, 0) + 1
        if self.on_get:
            self.on_get(key, self.get_counts[key])
        return copy.deepcopy(self.objects.get(key))

    def command(self, arguments, payload=None):
        # 模拟真实 JSON 传输的值复制，不能让假 API 与预期输入共享可变字典。
        payload = copy.deepcopy(payload)
        if arguments[:2] == ['get', 'nodes']:
            self.node_reads += 1
            if self.on_nodes:
                self.on_nodes(self.node_reads)
            return {'items': copy.deepcopy(self.nodes)}
        verb = next(value for value in arguments if value in ('patch', 'create'))
        index = arguments.index(verb)
        kind = arguments[index + 1] if verb == 'patch' else payload['kind']
        name = arguments[index + 2] if verb == 'patch' else payload['metadata']['name']
        key = (kind, name)
        self.writes.append(key)
        if self.before_write:
            self.before_write(key)
        if self.reject_write == kind:
            raise TRUST.TrustFailure('kubernetes_command_failed')
        if verb == 'create':
            if key in self.objects:
                raise TRUST.TrustFailure('kubernetes_command_failed')
            value = copy.deepcopy(payload)
            value['metadata'].update(uid=kind + '-uid', resourceVersion='1')
            self.objects[key] = value
        else:
            value = self.objects[key]
            for operation in payload[:2]:
                field = operation['path'].rsplit('/', 1)[1]
                if value['metadata'][field] != operation['value']:
                    raise TRUST.TrustFailure('kubernetes_command_failed')
            value['metadata']['annotations'] = payload[2]['value']
            value[payload[3]['path'][1:]] = payload[3]['value']
            value['metadata']['resourceVersion'] = str(int(value['metadata']['resourceVersion']) + 1)
        if self.after_write:
            self.after_write(key)
        if self.lose_response == kind:
            raise TRUST.TrustFailure('kubernetes_command_failed')
        return copy.deepcopy(self.objects[key])


class GatewayTrustTests(unittest.TestCase):
    def setUp(self):
        self.client = FakeClient()
        self.state = {'networkPolicy': 'not_attempted', 'runtimeConfig': 'not_attempted'}

    def configure(self):
        return TRUST.configure(self.client, PRIMARY, self.state)

    def failed(self, code):
        with self.assertRaises(TRUST.TrustFailure) as failure:
            self.configure()
        self.assertEqual(str(failure.exception), code)

    def test_replaces_entire_trust_set_preserving_unrelated_configuration(self):
        result = self.configure()
        data = self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['data']
        self.assertEqual(data, {'Other': 'retained', 'TrustedProxy__Enabled': 'true',
            'TrustedProxy__ForwardLimit': '1', 'TrustedProxy__KnownProxies__0': PRIMARY,
            'TrustedProxy__KnownProxies__1': '10.42.0.0'})
        self.assertEqual(self.client.writes, [('NetworkPolicy', TRUST.POLICY), ('ConfigMap', TRUST.CONFIGURATION)])
        self.assertEqual(self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['metadata']['annotations']['unrelated'], 'retained')
        self.assertTrue(result['apiRolloutRequired'])
        self.assertFalse(result['runtimeActivationVerified'])
        self.assertEqual(result['writeResponsesRecovered'], [])

    def test_idempotent_retry_performs_no_write(self):
        self.configure()
        previous = copy.deepcopy(self.client.objects)
        self.client.writes.clear()
        self.configure()
        self.assertEqual(self.client.writes, [])
        self.assertEqual(self.client.objects, previous)

    def test_missing_or_ambiguous_gateway_node_is_rejected(self):
        for nodes in ([], [self.client.nodes[0], self.client.nodes[0]]):
            with self.subTest(count=len(nodes)):
                self.client.nodes = nodes
                self.failed('gateway_node_not_unique')
                self.assertEqual(self.client.writes, [])

    def test_not_ready_or_deleting_gateway_is_rejected(self):
        self.client.nodes[0]['status']['conditions'][0]['status'] = 'False'
        self.failed('gateway_node_not_ready')
        self.client.nodes[0]['status']['conditions'][0]['status'] = 'True'
        self.client.nodes[0]['metadata']['deletionTimestamp'] = 'now'
        self.failed('gateway_node_not_ready')
        self.assertEqual(self.client.writes, [])

    def test_invalid_or_overlapping_pod_cidr_is_rejected(self):
        for cidr in ('0.0.0.0/0', '192.168.1.0/24', '10.42.0.1/32', '224.0.0.0/24'):
            with self.subTest(cidr=cidr):
                self.client.nodes[0]['spec']['podCIDR'] = cidr
                self.failed('gateway_pod_cidr_invalid')
                self.assertEqual(self.client.writes, [])

    def test_wrong_configuration_identity_is_rejected(self):
        self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['metadata']['namespace'] = 'different'
        self.failed('resource_identity_invalid')
        self.assertEqual(self.client.writes, [])

    def test_immutable_configuration_is_rejected_before_policy_write(self):
        self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['immutable'] = True
        self.failed('runtime_configuration_immutable')
        self.assertEqual(self.client.writes, [])

    def test_config_version_conflict_preserves_concurrent_data_and_reports_partial_state(self):
        def concurrent(key):
            if key[0] == 'ConfigMap':
                self.client.objects[key]['metadata']['resourceVersion'] = '2'
                self.client.objects[key]['data']['Concurrent'] = 'retained'
        self.client.before_write = concurrent
        self.failed('resource_write_not_verified')
        self.assertEqual(self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['data']['Concurrent'], 'retained')
        self.assertEqual(self.state, {'networkPolicy': 'verified', 'runtimeConfig': 'not_verified'})

    def test_policy_conflict_does_not_write_configuration(self):
        policy = resource('NetworkPolicy', TRUST.POLICY, spec={'podSelector': {}, 'ingress': [{}]})
        self.client.objects[('NetworkPolicy', TRUST.POLICY)] = policy
        self.client.before_write = lambda key: policy['metadata'].update(resourceVersion='2')
        self.failed('resource_write_not_verified')
        self.assertEqual(self.client.writes, [('NetworkPolicy', TRUST.POLICY)])
        self.assertEqual(self.state['runtimeConfig'], 'not_attempted')

    def test_config_lost_response_is_verified_by_operation_marker_and_contents(self):
        self.client.lose_response = 'ConfigMap'
        result = self.configure()
        self.assertEqual(result['writeResponsesRecovered'], ['runtimeConfig'])

    def test_policy_create_lost_response_is_verified_by_readback(self):
        self.client.lose_response = 'NetworkPolicy'
        result = self.configure()
        self.assertEqual(result['writeResponsesRecovered'], ['networkPolicy'])

    def test_rejected_write_cannot_be_promoted_to_success(self):
        self.client.reject_write = 'ConfigMap'
        self.failed('resource_write_not_verified')
        self.assertEqual(self.state['networkPolicy'], 'verified')
        self.assertEqual(self.state['runtimeConfig'], 'not_verified')

    def test_tampered_write_readback_is_rejected(self):
        def tamper(key):
            if key[0] == 'ConfigMap':
                self.client.objects[key]['data']['TrustedProxy__KnownProxies__9'] = '10.42.0.99'
        self.client.after_write = tamper
        self.failed('resource_write_not_verified')
        self.assertEqual(self.state['runtimeConfig'], 'not_verified')

    def test_final_readback_rejects_configuration_changed_after_write(self):
        def change(key, count):
            if key[0] == 'ConfigMap' and count == 3:
                self.client.objects[key]['data']['Concurrent'] = 'changed'
        self.client.on_get = change
        self.failed('resource_changed_after_write')
        self.assertEqual(self.state['runtimeConfig'], 'not_verified')

    def test_node_changed_before_write_is_rejected(self):
        self.client.on_nodes = lambda count: self.client.nodes[0]['metadata'].update(uid='new') if count == 2 else None
        self.failed('gateway_node_changed')
        self.assertEqual(self.client.writes, [])

    def test_binary_trust_configuration_is_rejected(self):
        self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['binaryData'] = {'TrustedProxy__KnownProxies__9': 'eA=='}
        self.failed('binary_trust_configuration_invalid')
        self.assertEqual(self.client.writes, [])

    def test_replaced_resource_uid_is_rejected_without_overwriting_replacement(self):
        def replace(key):
            if key[0] == 'ConfigMap':
                self.client.objects[key]['metadata']['uid'] = 'replacement'
        self.client.before_write = replace
        self.failed('resource_write_not_verified')
        self.assertEqual(self.client.objects[('ConfigMap', TRUST.CONFIGURATION)]['metadata']['uid'], 'replacement')

    def test_main_rejects_unqualified_context_root_and_invalid_address_without_commands(self):
        for arguments, uid in (([PRIMARY, '--kubectl', 'kubectl'], 0), ([PRIMARY], 1000), (['0.0.0.0'], 0)):
            with self.subTest(arguments=arguments):
                out, err = io.StringIO(), io.StringIO()
                with patch.object(TRUST.os, 'geteuid', return_value=uid), patch.object(TRUST.subprocess, 'run') as command, contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                    self.assertEqual(TRUST.main(arguments), 1)
                self.assertEqual(out.getvalue(), '')
                self.assertEqual(json.loads(err.getvalue())['runtimeConfig'], 'not_attempted')
                command.assert_not_called()

    def test_client_command_has_deadline_and_never_echoes_raw_error(self):
        for outcome in (subprocess.CompletedProcess([], 1, stdout='private-response', stderr='private-credential'),
                        subprocess.TimeoutExpired('private-command', 20)):
            with self.subTest(outcome=type(outcome).__name__):
                with patch.object(TRUST.subprocess, 'run', side_effect=outcome if isinstance(outcome, Exception) else None,
                                  return_value=outcome) as command:
                    with self.assertRaises(TRUST.TrustFailure) as failure:
                        TRUST.Client(['kubectl']).command(['get', 'nodes'])
                self.assertEqual(str(failure.exception), 'kubernetes_command_failed')
                self.assertEqual(command.call_args.kwargs['timeout'], 20)
                self.assertTrue(command.call_args.kwargs['capture_output'])


if __name__ == '__main__':
    unittest.main()
