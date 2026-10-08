import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

async function fixture(t) {
  const cwd = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-reuse-'));
  t.after(() => rm(cwd, { recursive: true, force: true }));
  execFileSync('git', ['init', '--quiet'], { cwd });
  await writeFile(path.join(cwd, '.gitignore'), 'bin/\n.tmp/\n');
  await mkdir(path.join(cwd, 'src'), { recursive: true });
  await writeFile(path.join(cwd, 'src/source.cs'), 'class First {}');
  await writeFile(path.join(cwd, 'test.csproj'), '<Project />');
  const assembly = 'bin/Release/test.dll';
  let builds = 0;
  const options = {
    cwd, project: 'test.csproj', assembly,
    args: ['build', 'test.csproj', '-c', 'Release'], sdkVersion: '10.0.401',
    build: async () => {
      builds++;
      await mkdir(path.dirname(path.join(cwd, assembly)), { recursive: true });
      await writeFile(path.join(cwd, assembly), `compiled-${builds}`);
      await writeFile(path.join(cwd, 'bin/Release/dependency.dll'), 'dependency');
    }
  };
  const { prepareTestBuild } = await import('../../scripts/testing/test-build-reuse.mjs');
  return { cwd, options, run: extra => prepareTestBuild({ ...options, ...extra }), builds: () => builds };
}

test('相同源码和 Release 产物只构建一次，测试筛选变化不重新构建', async t => {
  const f = await fixture(t);
  assert.equal((await f.run()).reused, false);
  assert.equal((await f.run()).reused, true);
  assert.equal(f.builds(), 1);
});

test('源码、未跟踪输入、依赖产物或 SDK 改变必须重新构建', async t => {
  const f = await fixture(t);
  await f.run();
  await writeFile(path.join(f.cwd, 'src/source.cs'), 'class Changed {}');
  assert.equal((await f.run()).reused, false);
  await writeFile(path.join(f.cwd, 'src/new.cs'), 'class Added {}');
  assert.equal((await f.run()).reused, false);
  await writeFile(path.join(f.cwd, 'bin/Release/dependency.dll'), 'changed');
  assert.equal((await f.run()).reused, false);
  assert.equal((await f.run({ sdkVersion: '10.0.402' })).reused, false);
  assert.equal(f.builds(), 5);
});

test('文档和测试结果变化可复用，删除编译输入不可复用', async t => {
  const f = await fixture(t);
  await f.run();
  await mkdir(path.join(f.cwd, 'docs'), { recursive: true });
  await writeFile(path.join(f.cwd, 'docs/report.md'), '报告');
  await mkdir(path.join(f.cwd, 'bin/Release/TestResults'), { recursive: true });
  await writeFile(path.join(f.cwd, 'bin/Release/TestResults/report.trx'), '结果');
  assert.equal((await f.run()).reused, true);
  await rm(path.join(f.cwd, 'src/source.cs'));
  assert.equal((await f.run()).reused, false);
});

test('校验式 no-build 拒绝没有记录或输入已改变的程序集', async t => {
  const f = await fixture(t);
  await assert.rejects(f.run({ mode: 'verify' }), /构建|记录/);
  await f.run();
  assert.equal((await f.run({ mode: 'verify' })).reused, true);
  await writeFile(path.join(f.cwd, 'src/source.cs'), 'class Later {}');
  await assert.rejects(f.run({ mode: 'verify' }), /构建|输入/);
  assert.equal(f.builds(), 1);
});

test('失败或构建中源码变化不产生可复用记录', async t => {
  const f = await fixture(t);
  await assert.rejects(f.run({ build: async () => { throw new Error('build failed'); } }), /build failed/);
  await assert.rejects(f.run({ build: async () => {
    await f.options.build();
    await writeFile(path.join(f.cwd, 'src/source.cs'), 'class DuringBuild {}');
  } }), /构建期间/);
  await assert.rejects(f.run({ mode: 'verify' }), /构建|记录/);
});

test('构建记录损坏不能绕过新鲜性校验', async t => {
  const f = await fixture(t);
  const result = await f.run();
  await writeFile(result.recordPath, '{broken');
  await assert.rejects(f.run({ mode: 'verify' }), /构建|记录/);
  await f.run();
  await assert.doesNotReject(async () => JSON.parse(await readFile(result.recordPath, 'utf8')));
});

test('嵌入资源与构建参数改变不能复用', async t => {
  const f = await fixture(t);
  await f.run();
  await writeFile(path.join(f.cwd, 'src/resource.md'), 'embedded');
  assert.equal((await f.run()).reused, false);
  assert.equal((await f.run({ args: [...f.options.args, '-p:DefineConstants=CHANGED'] })).reused, false);
});

test('自定义 MSBuild 环境属性变化必须失效，失败重建不能留下旧记录', async t => {
  const f = await fixture(t);
  const before = process.env.FullNetAotAnalysis;
  try {
    delete process.env.FullNetAotAnalysis;
    await f.run();
    process.env.FullNetAotAnalysis = 'true';
    await assert.rejects(f.run({ mode: 'verify' }), /构建|输入/);
    assert.equal((await f.run()).reused, false);
    await assert.rejects(f.run({ mode: 'fresh', build: async () => { throw new Error('failed rebuild'); } }), /failed rebuild/);
    await assert.rejects(f.run({ mode: 'verify' }), /构建|记录/);
  } finally {
    if (before === undefined) delete process.env.FullNetAotAnalysis;
    else process.env.FullNetAotAnalysis = before;
  }
});

test('Git 忽略的项目 user 文件新增、修改和删除均使构建失效', async t => {
  const f = await fixture(t);
  await writeFile(path.join(f.cwd, '.gitignore'), 'bin/\n.tmp/\n*.user\n');
  await f.run();
  const user = path.join(f.cwd, 'test.csproj.user');
  for (const constants of ['FIRST', 'SECOND']) {
    await writeFile(user, `<Project><PropertyGroup><DefineConstants>${constants}</DefineConstants></PropertyGroup></Project>`);
    await assert.rejects(f.run({ mode: 'verify' }), /构建|输入/);
    assert.equal((await f.run()).reused, false);
  }
  await rm(user);
  await assert.rejects(f.run({ mode: 'verify' }), /构建|输入/);
  assert.equal((await f.run()).reused, false);
});

test('pnpm 测试选择及缓存模式不改变构建环境，真实 MSBuild 属性仍参与校验', async t => {
  const f = await fixture(t);
  const before = process.env.npm_lifecycle_script;
  try {
    process.env.npm_lifecycle_script = 'node run.mjs --reuse-build --selection first';
    await f.run({ build: async env => {
      assert.ok(env);
      assert.equal(env?.npm_lifecycle_script, undefined);
      assert.ok(process.env.npm_lifecycle_script);
      await f.options.build();
    } });
    process.env.npm_lifecycle_script = 'node run.mjs --reuse-build --selection second';
    assert.equal((await f.run()).reused, true);
    process.env.npm_lifecycle_script = 'node run.mjs --no-build --selection second';
    assert.equal((await f.run({ mode: 'verify' })).reused, true);
    assert.equal(f.builds(), 1);
  } finally {
    if (before === undefined) delete process.env.npm_lifecycle_script;
    else process.env.npm_lifecycle_script = before;
  }
});
