import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const uniRequire = createRequire(new URL('../clients/uniapp/package.json', import.meta.url));
const cliRequire = createRequire(uniRequire.resolve('@dcloudio/uni-cli-shared/package.json'));
const watcherRequire = createRequire(cliRequire.resolve('chokidar/package.json'));
const braces = watcherRequire('braces');

function nestedAst(depth) {
  let node = { type: 'text', value: 'x' };
  for (let index = 0; index < depth; index += 1) node = { type: 'root', nodes: [node] };
  return node;
}

for (const method of ['parse', 'compile', 'expand', 'stringify']) {
  test(`${method} rejects adversarial nesting before native stack exhaustion`, () => {
    const input = '{'.repeat(3_000) + 'x' + '}'.repeat(3_000);
    assert.throws(() => braces[method](input), error => error instanceof SyntaxError && error.code === 'ERR_BRACES_DEPTH_LIMIT');
  });
}
for (const method of ['compile', 'expand', 'stringify']) {
  test(`${method} bounds caller-supplied AST depth and cycles`, () => {
    assert.throws(() => braces[method](nestedAst(12_000)), error => error.code === 'ERR_BRACES_DEPTH_LIMIT');
    rejectsInChild(`const cyclic = { type: 'root', nodes: [] }; cyclic.nodes.push(cyclic); braces.${method}(cyclic);`, 'ERR_BRACES_INVALID_AST');
  });
}

test('ordinary alternatives, ranges, escaping and AST consumers retain their results', () => {
  assert.deepEqual(braces.expand('src/{a,b}/{01..03}.vue'), [
    'src/a/01.vue', 'src/a/02.vue', 'src/a/03.vue', 'src/b/01.vue', 'src/b/02.vue', 'src/b/03.vue'
  ]);
  assert.equal(braces.compile('static/{images,fonts}/**/*'), 'static/(images|fonts)/**/*');
  assert.deepEqual(braces.expand(String.raw`a/\{literal\}/b`), ['a/{literal}/b']);
  assert.equal(braces.stringify(braces.parse('a/{b,c}/d')), 'a/{b,c}/d');
  assert.equal(braces.compile(nestedAst(16)), 'x');
});

for (const input of ['('.repeat(3_000) + 'x', '{'.repeat(3_000) + 'x']) {
  test('unclosed nested blocks fail before recursive parser fallback', () => {
    assert.throws(() => braces.parse(input), error => error.code === 'ERR_BRACES_DEPTH_LIMIT');
  });
}

test('depth limit cannot be relaxed through caller options and quoted braces remain literal', () => {
  assert.throws(() => braces.compile('{'.repeat(100) + 'x' + '}'.repeat(100), { maxDepth: Infinity }),
    error => error.code === 'ERR_BRACES_DEPTH_LIMIT');
  assert.equal(braces.stringify(braces.parse('"' + '{'.repeat(100) + '"')), '{'.repeat(100));
});

test('external AST parent cycles are rejected before expansion backtracking', () => {
  rejectsInChild(`const cyclic = { type: 'branch', nodes: [] }; cyclic.parent = cyclic; braces.expand(cyclic);`, 'ERR_BRACES_INVALID_AST');
});

// 子进程硬超时使拒绝服务回归也能报告失败，不把整个测试进程挂死。
function rejectsInChild(expression, code) {
  const script = `const assert = require('node:assert/strict'); const braces = require(${JSON.stringify(watcherRequire.resolve('braces'))}); assert.throws(() => { ${expression} }, error => error.code === ${JSON.stringify(code)});`;
  const result = spawnSync(process.execPath, ['-e', script], { encoding: 'utf8', timeout: 2_000, windowsHide: true });
  assert.equal(result.error, undefined, result.error?.message);
  assert.equal(result.status, 0, result.stderr);
}

test('non-array AST node lists cannot bypass validation through custom iterables', () => {
  for (const method of ['compile', 'expand', 'stringify']) {
    rejectsInChild(`const root = { type: 'root' }; root.nodes = new Set([root]); braces.${method}(root);`, 'ERR_BRACES_INVALID_AST');
  }
});

test('shared subtrees, cyclic values and foreign parent queues are rejected within the deadline', () => {
  for (const method of ['compile', 'expand', 'stringify']) {
    rejectsInChild(`let root = { type: 'text', value: 'x' }; for (let i = 0; i < 60; i++) root = { type: 'root', nodes: [root, root] }; braces.${method}(root);`, 'ERR_BRACES_INVALID_AST');
    rejectsInChild(`const value = []; value.push(value); braces.${method}({ type: 'root', nodes: [{ type: 'text', value }] });`, 'ERR_BRACES_INVALID_AST');
  }
  rejectsInChild(`const queue = []; queue.push(queue); const foreign = { type: 'root', queue }; braces.expand({ type: 'paren', parent: foreign, nodes: [{ type: 'text', value: 'x' }] });`, 'ERR_BRACES_INVALID_AST');
});

test('shallow unmatched blocks keep literal expansion during parser fallback', () => {
  for (const input of ['{a', 'foo/{bar', '(a', 'foo/(bar', '{a,(b', 'a/{b,c']) {
    assert.deepEqual(braces.expand(input), [input]);
  }
});
