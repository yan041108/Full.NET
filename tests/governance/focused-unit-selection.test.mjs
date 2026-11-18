import assert from 'node:assert/strict';
import { readFile, mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { focusedUnitSelection, validateFocusedUnitSource, validateFocusedUnitFiles } from '../../scripts/testing/run-focused-unit.mjs';

const target = 'Full.NET.UnitTests.Organization.OrganizationPositionWorkbookCodecTests';

test('正式测试类可以并列其他 MSTest 特性，但其他特性不能替代 TestClass', () => {
  const selection = focusedUnitSelection(target, '1', 'Sample');
  const declaration = 'public sealed class OrganizationPositionWorkbookCodecTests { [TestMethod] public void Sample() {} }';
  for (const attributes of ['[TestClass] [DoNotParallelize]', '[DoNotParallelize] [TestClass]', '[TestClass] [TestCategory("scope")] [DoNotParallelize]'])
    validateFocusedUnitSource(selection, 'namespace Full.NET.UnitTests.Organization; ' + attributes + ' ' + declaration);
  assert.throws(() => validateFocusedUnitSource(selection, 'namespace Full.NET.UnitTests.Organization; [DoNotParallelize] ' + declaration));
});

test('分部方法必须属于同命名空间同名分部类，普通类不能借用其他文件的方法', () => {
  const selected = focusedUnitSelection(target, '1', 'Continued');
  const owner = 'namespace Full.NET.UnitTests.Organization; [TestClass] public sealed partial class OrganizationPositionWorkbookCodecTests {}';
  const part = 'namespace Full.NET.UnitTests.Organization; public sealed partial class OrganizationPositionWorkbookCodecTests { [TestMethod] public void Continued() {} }';
  validateFocusedUnitSource(selected, owner, [part]);
  for (const invalid of [part.replace('UnitTests.Organization;', 'UnitTests.Other;'),
    part.replace('WorkbookCodecTests {', 'WorkbookCodecTestsExtra {'), part.replace('partial class', 'class')])
    assert.throws(() => validateFocusedUnitSource(selected, owner, [invalid]));
  assert.throws(() => validateFocusedUnitSource(selected, owner.replace('partial class', 'class'), [part]));
});

test('实际声明支持非同名文件、分部方法并拒绝重复正式归属', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-focused-source-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const directory = path.join(root, 'tests/Full.NET.UnitTests/Organization');
  await mkdir(directory, { recursive: true });
  const selected = focusedUnitSelection(target, '1', 'Continued');
  const owner = 'namespace Full.NET.UnitTests.Organization; [TestClass] public sealed partial class OrganizationPositionWorkbookCodecTests {}';
  await writeFile(path.join(directory, 'DifferentName.cs'), owner);
  await writeFile(path.join(directory, 'Continuation.cs'), 'namespace Full.NET.UnitTests.Organization; public sealed partial class OrganizationPositionWorkbookCodecTests { [TestMethod] public void Continued() {} }');
  await validateFocusedUnitFiles(selected, root);
  await writeFile(path.join(directory, 'Duplicate.cs'), owner);
  await assert.rejects(validateFocusedUnitFiles(selected, root), /唯一/u);
});

test('真实分部 AI 场景和非同名 Messaging 类都可以精确定位', async () => {
  for (const [type, method] of [
    ['Full.NET.UnitTests.Ai.AiKnowledgeDocumentPlanningServiceTests', 'Fresh_docx_generation_freezes_complete_blocks_pays_each_batch_and_activates_once'],
    ['Full.NET.UnitTests.Messaging.MessagingAuthorizationContributorTests', 'Contributor_publishes_exact_messaging_permissions_actions_and_navigation']
  ]) await validateFocusedUnitFiles(focusedUnitSelection(type, '1', method));
});

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
