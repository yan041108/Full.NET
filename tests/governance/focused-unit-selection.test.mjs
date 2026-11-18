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
