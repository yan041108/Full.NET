import assert from 'node:assert/strict';
import test from 'node:test';
import { renderRuntimeNotices } from '../runtime-notices.mjs';
test('许可产物不依赖模块和许可文件的枚举顺序', () => {
  const entries = [{ name: 'b', version: '1', file: 'LICENSE', text: 'B' }, { name: 'a', version: '2', file: 'NOTICE', text: 'A notice' }, { name: 'a', version: '2', file: 'LICENSE', text: 'A license' }];
  const expected = '原始许可\n\na@2\nA license\n\na@2\nA notice\n\nb@1\nB\n\n内置依赖许可';
  assert.equal(renderRuntimeNotices('原始许可', entries, '内置依赖许可'), expected);
  assert.equal(renderRuntimeNotices('原始许可', entries.toReversed(), '内置依赖许可'), expected);
});
test('保留每个版本的许可原文，不修改作者和换行', () => {
  const original = 'Copyright 作者\r\nAll rights reserved.\r\n';
  const value = renderRuntimeNotices('', [{ name: 'a', version: '1', file: 'LICENSE', text: original }, { name: 'a', version: '2', file: 'LICENSE', text: original }], '');
  assert.equal(value.split(original).length - 1, 2);
  assert.ok(value.includes('a@1')); assert.ok(value.includes('a@2'));
});
