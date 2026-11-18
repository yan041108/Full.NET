import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { validateCodegenFixturePreview } from '../../scripts/testing/codegen-fixture-preview.mjs';

const name = 'tests/Shared/CodeGeneration/Remediation/ProductFeature.g.cs';
function fixture() {
  const content = '// 规范生成内容\n';
  return { schemaVersion: 1, scope: 'remediation', generatorVersion: '2026.11.18.1', ownedPaths: [name],
    changes: [{ path: name, beforeHash: '0'.repeat(64), afterHash: createHash('sha256').update(content).digest('hex'), content }] };
}

test('带摘要的只读生成预览保持输入不变，也允许确实缺失的原文件', () => {
  const value = fixture(); const original = JSON.stringify(value);
  assert.deepEqual(validateCodegenFixturePreview(value), { owned: 1, changed: 1, bytes: Buffer.byteLength(value.changes[0].content) });
  assert.equal(JSON.stringify(value), original);
  value.changes[0].beforeHash = null; assert.equal(validateCodegenFixturePreview(value).changed, 1);
});

test('实际聚合服务产物属于明确支持的生成文件类型', () => {
  const value = fixture(); value.ownedPaths = ['ProductAggregate.g.cs', 'OrganizationParentAggregate.g.cs']
    .map(file => 'tests/Shared/CodeGeneration/Remediation/' + file); value.changes = [];
  assert.deepEqual(validateCodegenFixturePreview(value), { owned: 2, changed: 0, bytes: 0 });
});

test('越界、路径穿越、悬空和重复所有权不能进入预览', () => {
  for (const path of ['src/secret.cs', name.replace('ProductFeature', '../ProductFeature'), name + '/other', name.replace('ProductFeature', 'ProductEndpoint')]) {
    const value = fixture(); value.ownedPaths = [path]; value.changes[0].path = path;
    assert.throws(() => validateCodegenFixturePreview(value));
  }
  const duplicate = fixture(); duplicate.ownedPaths.push(name); assert.throws(() => validateCodegenFixturePreview(duplicate));
  const missing = fixture(); missing.changes[0].path = name.replace('Product', 'Other'); assert.throws(() => validateCodegenFixturePreview(missing));
});

test('内容篡改、非规范换行、虚假无变化及重复差异必须拒绝', () => {
  for (const mutate of [value => { value.changes[0].content += 'tampered'; },
    value => { value.changes[0].content += '\r\n'; }, value => { value.changes[0].beforeHash = value.changes[0].afterHash; },
    value => { value.changes.push(value.changes[0]); }, value => { value.changes[0].afterHash = 'invalid'; }]) {
    const value = fixture(); mutate(value); assert.throws(() => validateCodegenFixturePreview(value));
  }
});

test('超量或未知范围不能作为同一批生成结果', () => {
  for (const mutate of [value => { value.scope = 'all'; }, value => { value.schemaVersion = 2; },
    value => { value.changes[0].content = 'x'.repeat(256 * 1024 + 1); }, value => { value.ownedPaths = []; }]) {
    const value = fixture(); mutate(value); assert.throws(() => validateCodegenFixturePreview(value));
  }
});
