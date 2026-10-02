import assert from 'node:assert/strict';
import test from 'node:test';
import * as probe from '../../eng/testing/log-alert-notification-probe.mjs';

const namespace = 'fullnet-log-restart-12345678';
const pod = (uid, ready = true, ns = namespace) => ({ metadata: { uid, namespace: ns, name: 'local-alertmanager' },
  status: { conditions: [{ type: 'Ready', status: ready ? 'True' : 'False' }] } });

test('重建证明必须来自同一任务的新就绪 Pod', () => {
  assert.deepEqual(probe.assertReplacementIdentity(pod('old'), pod('new'), namespace),
    { previousUid: 'old', replacementUid: 'new', podReplaced: true, ready: true });
  assert.throws(() => probe.assertReplacementIdentity(pod('old'), pod('old'), namespace));
  assert.throws(() => probe.assertReplacementIdentity(pod('old'), pod('new', false), namespace));
  assert.throws(() => probe.assertReplacementIdentity(pod('old'), pod('new', true, 'other'), namespace));
  assert.throws(() => probe.assertReplacementIdentity(pod('old'), pod(undefined), namespace));
  assert.throws(() => probe.assertReplacementIdentity(pod('old'), pod('new'), 'production'));
});
