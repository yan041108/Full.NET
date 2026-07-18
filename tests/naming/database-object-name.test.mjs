import assert from 'node:assert/strict';
import test from 'node:test';
import { buildDatabaseObjectName } from '../../scripts/naming/database-object-name.mjs';

test('短数据库对象名保持原值', () => {
  assert.equal(buildDatabaseObjectName('IX_fn_identity_user_Username'), 'IX_fn_identity_user_Username');
});

test('长数据库对象名使用固定 SHA-256 摘要压缩', () => {
  assert.equal(
    buildDatabaseObjectName('IX_fn_notifications_delivery_attempt_SubscriptionId_RequestedAtUtc_ChannelProvider'),
    'IX_fn_notifications_delivery_attempt_SubscriptionId_Req_5b137a8d'
  );
});

test('数据库对象名拒绝非 ASCII、空值和未知对象前缀', () => {
  assert.throws(() => buildDatabaseObjectName(''), /不能为空/);
  assert.throws(() => buildDatabaseObjectName('IX_fn_identity_用户'), /ASCII/);
  assert.throws(() => buildDatabaseObjectName('KEY_fn_identity_user_Id'), /对象前缀/);
});

test('摘要结果不依赖当前文化或重复调用次数', () => {
  const input = 'FK_fn_notifications_delivery_attempt_SubscriptionId_NotificationSubscriptionId';
  const outputs = new Set(Array.from({ length: 100 }, () => buildDatabaseObjectName(input)));
  assert.equal(outputs.size, 1);
  assert.equal([...outputs][0].length, 64);
});
