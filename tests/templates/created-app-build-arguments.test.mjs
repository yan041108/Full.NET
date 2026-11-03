import assert from 'node:assert/strict';
import test from 'node:test';
import { createdAppBuildArguments } from './support/created-app-build-arguments.mjs';

test('独立应用构建显式使用准入预算且不修改原参数', () => {
  const args = ['build', 'Demo.csproj', '-c', 'Release'];
  assert.deepEqual(createdAppBuildArguments(args, { DOTNET_PROCESSOR_COUNT: '2' }), [...args, '-maxcpucount:2']);
  assert.equal(args.length, 4);
});

test('运行已有工具和未指定预算的命令保持原样', () => {
  for (const args of [['exec', 'cli.dll'], ['run', '--project', 'Demo.csproj']]) {
    assert.equal(createdAppBuildArguments(args, { DOTNET_PROCESSOR_COUNT: '2' }), args);
  }
  const args = ['build', 'Demo.csproj'];
  assert.equal(createdAppBuildArguments(args, {}), args);
});

test('错误的预算不能转成无界或无效构建开关', () => {
  for (const cpu of ['', '0', '-1', '2.5', '2 -m', '9007199254740992']) {
    assert.throws(() => createdAppBuildArguments(['build'], { DOTNET_PROCESSOR_COUNT: cpu }), /正整数/u);
  }
});
