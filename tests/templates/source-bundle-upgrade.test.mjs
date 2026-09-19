import assert from 'node:assert/strict';
import test from 'node:test';
import { compareFrameworkVersions } from '../../scripts/templates/framework-manifest-utils.mjs';

test('compareFrameworkVersions orders semantic versions', () => {
  assert.equal(compareFrameworkVersions('0.1.0', '0.1.0'), 0);
  assert.ok(compareFrameworkVersions('0.1.1', '0.1.0') > 0);
  assert.ok(compareFrameworkVersions('0.1.0', '0.2.0') < 0);
});

test('compareFrameworkVersions rejects invalid input', () => {
  assert.throws(() => compareFrameworkVersions('bad', '0.1.0'), /Invalid framework version/);
});