import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { focusedUnitSelection, validateFocusedUnitSource } from '../../scripts/testing/run-focused-unit.mjs';

const target = 'Full.NET.UnitTests.Organization.OrganizationPositionWorkbookCodecTests';

test('聚焦入口绑定实际测试源码与完整类型分隔符', async () => {
  const selected = focusedUnitSelection(target, '6');
  validateFocusedUnitSource(selected, await readFile(new URL('../../' + selected.sourcePath, import.meta.url), 'utf8'));
  assert.deepEqual(selected.args, ['unit', '--filter', `FullyQualifiedName~${target}.`, '--minimum-expected-tests', '6']);
});

test('空值、命名空间、通配符及复合筛选不能触发构建', () => {
  for (const value of [undefined, '', 'Full.NET.UnitTests', 'Full.NET.UnitTests.Organization', target + '*', target + '|FullyQualifiedName~Other', '../' + target]) {
    assert.throws(() => focusedUnitSelection(value, '1'));
  }
});

test('聚焦入口拒绝无效发现数量', () => {
  for (const value of [undefined, '', '0', '-1', '1.5', '1e3', '9007199254740992']) assert.throws(() => focusedUnitSelection(target, value));
});

test('普通类、错误命名空间和同名前缀类不能冒充指定测试类', () => {
  const selection = focusedUnitSelection(target, '6');
  for (const source of [
    'namespace Full.NET.UnitTests.Organization; public sealed class OrganizationPositionWorkbookCodecTests {}',
    'namespace Other; [TestClass] public sealed class OrganizationPositionWorkbookCodecTests {}',
    'namespace Full.NET.UnitTests.Organization; [TestClass] public sealed class OrganizationPositionWorkbookCodecTestsExtra {}'
  ]) assert.throws(() => validateFocusedUnitSource(selection, source));
});

test('可按精确方法选择其全部数据行，拒绝不存在的方法和筛选表达式', async () => {
  const method = 'Compatible_missing_and_sparse_row_metadata_keep_existing_business_rows';
  const selection = focusedUnitSelection(target, '6', method);
  const source = await readFile(new URL('../../' + selection.sourcePath, import.meta.url), 'utf8');
  validateFocusedUnitSource(selection, source);
  assert.equal(selection.args[2], `FullyQualifiedName=${target}.${method}`);
  assert.throws(() => validateFocusedUnitSource(focusedUnitSelection(target, '1', 'Missing'), source));
  for (const value of ['*', method + '|Other', 'Type.' + method, '']) {
    if (value) assert.throws(() => focusedUnitSelection(target, '1', value));
  }
});

test('多个精确方法合并为有界并集，拒绝空段、重复及越界数量', async () => {
  const names = ['Compatible_missing_and_sparse_row_metadata_keep_existing_business_rows', 'ParseImportAsync_rejects_unknown_headers_and_excess_rows'];
  const selection = focusedUnitSelection(target, '7', names.join(', '));
  validateFocusedUnitSource(selection, await readFile(new URL('../../' + selection.sourcePath, import.meta.url), 'utf8'));
  assert.equal(selection.args[2], names.map(name => `FullyQualifiedName=${target}.${name}`).join('|'));
  for (const value of ['a,', ',a', 'a,,b', 'a,a', Array.from({ length: 11 }, (_, index) => 'method' + index).join(',')]) {
    assert.throws(() => focusedUnitSelection(target, '1', value));
  }
});
