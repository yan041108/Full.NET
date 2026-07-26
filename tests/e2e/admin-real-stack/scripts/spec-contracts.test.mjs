import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { test } from 'node:test';
import { findForbiddenSessionContextLocators } from './spec-contracts.mjs';

test('识别直接选择 Full.NET Host 隐藏文本的真实栈断言', () => {
  const violations = findForbiddenSessionContextLocators(
    "await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();"
  );

  assert.deepEqual(violations, [1]);
});

test('允许通过双端可见上下文辅助函数断言 Host 上下文', () => {
  const violations = findForbiddenSessionContextLocators(
    "await expectVisibleCurrentContext(page, 'Full.NET Host');"
  );

  assert.deepEqual(violations, []);
});

test('真实栈 spec 统一通过可见上下文辅助函数断言 Host 上下文', async () => {
  const testsDirectory = path.resolve(import.meta.dirname, '../tests');
  const specFiles = (await readdir(testsDirectory))
    .filter(fileName => fileName.endsWith('.spec.mjs'))
    .sort();
  const violations = [];

  for (const fileName of specFiles) {
    const source = await readFile(path.join(testsDirectory, fileName), 'utf8');
    for (const lineNumber of findForbiddenSessionContextLocators(source)) {
      violations.push(`${fileName}:${lineNumber}`);
    }
  }

  assert.deepEqual(
    violations,
    [],
    '请复用 expectVisibleCurrentContext，避免命中 Vue 隐藏选项文本。'
  );
});
