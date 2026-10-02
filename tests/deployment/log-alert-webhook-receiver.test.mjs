import assert from 'node:assert/strict';
import test from 'node:test';
import { createReceiver, parseNotification } from '../../eng/testing/log-alert-webhook-receiver.mjs';

const namespace = 'fullnet-log-restart-unit';
const notification = status => ({ version: '4', receiver: 'local-test', status, truncatedAlerts: 0,
  alerts: [{ status, fingerprint: '123abc', startsAt: '2026-10-01T00:00:00Z', endsAt: '2026-10-01T00:01:00Z',
    labels: { namespace, pod: 'consumer-restart', alertname: 'FullNetLogConsumerRestarting', secret: 'must-not-retain' },
    annotations: { secret: 'must-not-retain' } }] });
test('持续故障从首个有效请求计时，截止前不确认接收，截止时可恢复', async () => {
  for (const holdMilliseconds of [-1, 300001, 0.5, NaN]) {
    assert.throws(() => createReceiver({ namespace, nonce: 'unit', holdMilliseconds }), RangeError);
  }
  assert.throws(() => createReceiver({ namespace, nonce: 'unit', holdMilliseconds: 60000, failFirst: 1 }), RangeError);
  let time = 0;
  const server = createReceiver({ namespace, nonce: 'unit', holdMilliseconds: 60000, now: () => time });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  try {
    const post = body => fetch(url + '/sink/unit', { method: 'POST', body: JSON.stringify(body) });
    assert.equal((await post({})).status, 400);
    time = 10000;
    assert.equal((await post(notification('firing'))).status, 503);
    time = 69999;
    assert.equal((await post(notification('firing'))).status, 503);
    assert.deepEqual(await (await fetch(url + '/events/unit')).json(), []);
    time = 70000;
    assert.equal((await post(notification('firing'))).status, 200);
    const attempts = await (await fetch(url + '/attempts/unit')).json();
    assert.deepEqual(attempts.map(({ code, elapsedMs }) => [code, elapsedMs]), [[503, 0], [503, 59999], [200, 60000]]);
    assert.doesNotMatch(JSON.stringify(attempts), /must-not-retain|annotations/);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
test('故障注入只拒绝前两次有效通知，不确认失败请求且保留有界尝试证明', async () => {
  for (const failFirst of [-1, 9, 1.5, NaN]) assert.throws(() => createReceiver({ namespace, nonce: 'unit', failFirst }), RangeError);
  const server = createReceiver({ namespace, nonce: 'unit', failFirst: 2 });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  try {
    const post = body => fetch(url + '/sink/unit', { method: 'POST', body: JSON.stringify(body) });
    assert.equal((await post({})).status, 400);
    assert.equal((await post(notification('firing'))).status, 503);
    assert.equal((await post(notification('firing'))).status, 503);
    assert.deepEqual(await (await fetch(url + '/events/unit')).json(), []);
    assert.equal((await post(notification('firing'))).status, 200);
    const attempts = await (await fetch(url + '/attempts/unit')).json();
    assert.deepEqual(attempts.map(attempt => attempt.code), [503, 503, 200]);
    assert.ok(attempts.every(attempt => attempt.fingerprint === '123abc' && attempt.status === 'firing'));
    assert.doesNotMatch(JSON.stringify(attempts), /must-not-retain|annotations/);
    assert.equal((await (await fetch(url + '/events/unit')).json()).length, 1);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
test('接收 firing/resolved 只保留关联证明，不保留原始秘密或注解', () => {
  for (const status of ['firing', 'resolved']) {
    const event = parseNotification(notification(status), namespace);
    assert.equal(event.status, status);
    assert.equal(event.fingerprint, '123abc');
    assert.doesNotMatch(JSON.stringify(event), /secret|must-not-retain|annotations/);
  }
});
test('拒绝其他命名空间、Pod、规则和组内不一致状态', () => {
  for (const mutate of [
    body => { body.alerts[0].labels.namespace = 'other'; },
    body => { body.alerts[0].labels.pod = 'other'; },
    body => { body.alerts[0].labels.alertname = 'other'; },
    body => { body.alerts[0].status = 'resolved'; },
  ]) {
    const body = notification('firing'); mutate(body);
    assert.throws(() => parseNotification(body, namespace));
  }
});
test('拒绝错误接收器、截断、多告警、无指纹和无效时间', () => {
  for (const mutate of [
    body => { body.receiver = 'other'; }, body => { body.version = '5'; },
    body => { body.truncatedAlerts = 1; }, body => { body.alerts.push(body.alerts[0]); },
    body => { body.alerts[0].fingerprint = ''; }, body => { body.alerts[0].endsAt = 'invalid'; },
  ]) {
    const body = notification('firing'); mutate(body);
    assert.throws(() => parseNotification(body, namespace));
  }
});
test('HTTP 路径和正文拒绝不会留下事件，100 条上限后失败关闭', async () => {
  const server = createReceiver({ namespace, nonce: 'unit' });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  try {
    const post = (route, body) => fetch(url + route, { method: 'POST', body: JSON.stringify(body) });
    assert.equal((await post('/sink/wrong', notification('firing'))).status, 404);
    assert.equal((await post('/sink/unit', {})).status, 400);
    assert.equal((await fetch(url + '/sink/unit', { method: 'POST', body: 'x'.repeat(65537) })).status, 413);
    assert.deepEqual(await (await fetch(url + '/events/unit')).json(), []);
    for (let count = 0; count < 100; count++) assert.equal((await post('/sink/unit', notification('firing'))).status, 200);
    assert.equal((await post('/sink/unit', notification('firing'))).status, 503);
    const events = await (await fetch(url + '/events/unit')).json();
    assert.equal(events.length, 100);
    assert.doesNotMatch(JSON.stringify(events), /must-not-retain/);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});
