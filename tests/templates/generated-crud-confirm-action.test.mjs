import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const require = createRequire(new URL('../../ui/admin/package.json', import.meta.url));
const ts = require('typescript');
const { parse } = require('vue/compiler-sfc');

async function confirmation(pageApi, state) {
  const source = await readFile(new URL('../Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/vue/productsView.vue', import.meta.url), 'utf8');
  const { descriptor, errors } = parse(source);
  assert.deepEqual(errors, []);
  const script = ts.createSourceFile('generated-view.ts', descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true);
  const action = script.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'confirmDelete');
  const binding = script.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
    .find(node => ts.isObjectBindingPattern(node.name) && node.name.elements.some(element => element.propertyName?.text === 'disable'));
  assert.ok(action && binding, '必须消费实际生成的动作及页面模型别名');
  // 执行原产物中的解构与确认函数，避免测试替身再次把未定义别名掩盖掉。
  const body = ts.transpileModule(`const ${binding.name.getText(script)} = pageApi;\n${action.getText(script)}`,
    { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None } }).outputText;
  return new Function('pageApi', 'deleteOpen', 'deleting', 'deleteTicket', body + '\nreturn confirmDelete;')(
    pageApi, state.open, state.row, 1);
}

test('生成页面确认通过真实解构别名调用原记录，成功后关闭', async () => {
  const record = { id: 'record', version: '1' }; const calls = [];
  const state = { open: { value: true }, row: { value: record } };
  const action = await confirmation({ disable: async row => { calls.push(row); return true; }, scopeVersion: { value: 1 } }, state);
  await action(); assert.deepEqual(calls, [record]); assert.equal(state.open.value, false);
});

test('生成页面保留失败确认，并拒绝迟到成功关闭新上下文弹窗', async () => {
  const state = { open: { value: true }, row: { value: { id: 'original' } } };
  await (await confirmation({ disable: async () => false, scopeVersion: { value: 1 } }, state))();
  assert.equal(state.open.value, true);
  let complete; const pending = new Promise(resolve => { complete = resolve; }); const scopeVersion = { value: 1 };
  const action = await confirmation({ disable: () => pending, scopeVersion }, state);
  const execution = action(); scopeVersion.value++; state.row.value = { id: 'new-context' };
  complete(true); await execution; assert.equal(state.open.value, true);
});

test('缺少确认或当前记录时生成动作不执行删除', async () => {
  for (const [open, row] of [[false, {}], [true, undefined]]) {
    let calls = 0;
    const action = await confirmation({ disable: async () => { calls++; return true; }, scopeVersion: { value: 1 } },
      { open: { value: open }, row: { value: row } });
    await action(); assert.equal(calls, 0);
  }
});
