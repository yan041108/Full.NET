import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { CRUD_ARTIFACTS, verifyApplicationCrudGeneration } from './support/application-crud-generation.mjs';

// 注入命令执行器只验证验收编排，真实 CLI 由独立应用 Actions 执行。
function runner(root, failure, calls) {
  const stages = ['build', 'preview', 'apply', 'repeat', 'conflict'];
  return (command, args, options) => {
    const stage = stages[calls.length];
    calls.push({ stage, command, args, options });
    if (failure === stage) return { status: 1, stdout: '', stderr: stage + ' failed' };
    if (stage === 'build') return { status: 0, stdout: '', stderr: '' };
    if (stage === 'apply') {
      for (const path of [...CRUD_ARTIFACTS, '.fullnet/codegeneration-manifest.json']) {
        mkdirSync(dirname(join(root, path)), { recursive: true });
        writeFileSync(join(root, path), path + '\n');
      }
    }
    if (stage === 'preview' && failure === 'preview-writes') {
      mkdirSync(join(root, 'backend'), { recursive: true });
      writeFileSync(join(root, 'backend/ProductSql.g.cs'), 'unexpected preview write');
    }
    if (stage === 'repeat' && failure === 'repeat-mutates') {
      writeFileSync(join(root, 'backend/Product.manual.cs'), 'manual file lost');
    }
    if (stage === 'conflict') {
      if (failure === 'conflict-mutates') writeFileSync(join(root, 'backend/ProductSql.g.cs'), 'customization lost');
      return { status: failure === 'conflict-accepted' ? 0 : 2,
        stdout: failure === 'wrong-conflict' ? 'Conflict backend/Other.g.cs\n' : 'Conflict backend/ProductSql.g.cs\n', stderr: '' };
    }
    return { status: 0, stdout: CRUD_ARTIFACTS.map((path) => `${stage === 'repeat' ? 'Unchanged' : 'Create'} ${path}`).join('\n'), stderr: '' };
  };
}

for (const failure of ['build', 'preview', 'apply', 'repeat', 'conflict-accepted', 'preview-writes', 'repeat-mutates', 'conflict-mutates', 'wrong-conflict']) {
  test(`generated application CRUD acceptance rejects ${failure}`, () => {
    const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-tooling-'));
    const calls = [];
    try {
      assert.throws(() => verifyApplicationCrudGeneration(root, {
        reportDirectory: join(root, 'evidence'), run: runner(root, failure, calls),
      }));
      const stage = failure.startsWith('conflict-') || failure === 'wrong-conflict' ? 'conflict' : failure.split('-')[0];
      const expectedStatus = ['conflict-mutates', 'wrong-conflict'].includes(failure) ? 2
        : ['conflict-accepted', 'preview-writes', 'repeat-mutates'].includes(failure) ? 0 : 1;
      assert.equal(JSON.parse(readFileSync(join(root, 'evidence', stage + '.json'), 'utf8')).status,
        expectedStatus);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('generated application CRUD acceptance runs its own CLI and protects regeneration content', () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-crud-tooling-'));
  const calls = [];
  try {
    const result = verifyApplicationCrudGeneration(root, {
      reportDirectory: join(root, 'evidence'), run: runner(root, null, calls),
    });
    assert.deepEqual(result, { artifacts: 14, conflictRejected: true });
    assert.equal(calls.length, 5);
    assert.deepEqual(calls.map(({ command, args }) => [command, args[0]]),
      [['dotnet', 'build'], ...Array.from({ length: 4 }, () => ['dotnet', 'exec'])]);
    assert.equal(calls[0].args[1], join(root, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj'));
    assert.ok(calls[1].args[1].endsWith('Full.NET.CodeGeneration.Cli.dll'));
    assert.ok(calls.every(({ options }) => options.cwd === root && options.windowsHide === true));
    assert.equal(calls[1].args.includes('--apply'), false);
    assert.equal(calls[2].args.includes('--apply'), true);
    assert.match(readFileSync(join(root, 'backend/Product.manual.cs'), 'utf8'), /人工业务文件/);
    assert.equal(readFileSync(join(root, 'backend/ProductSql.g.cs'), 'utf8'),
      'backend/ProductSql.g.cs\n', 'acceptance test comment leaked into later application stages');
    assert.equal(JSON.parse(readFileSync(join(root, 'evidence/conflict.json'), 'utf8')).status, 2);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
