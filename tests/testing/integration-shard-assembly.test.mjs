import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import os from 'node:os';
import * as checker from '../../scripts/testing/verify-integration-shards.mjs';

test('默认保持原 Integration 程序集路径',()=>{
  assert.equal(checker.parseAssemblyArguments([]),path.join('tests','Full.NET.IntegrationTests','bin','Release','net10.0','Full.NET.IntegrationTests.dll'));
});
test('显式隔离程序集路径保留空格并解析绝对路径',()=>{
  const file=path.join(os.tmpdir(),'isolated integration','Full.NET.IntegrationTests.dll');
  assert.equal(checker.parseAssemblyArguments(['--assembly',file]),file);
});
test('相对程序集路径相对于调用目录解析',()=>{
  assert.equal(checker.parseAssemblyArguments(['--assembly','output/current.dll']),path.resolve('output/current.dll'));
});
test('拒绝缺少路径、空值、未知选项及多余参数',()=>{
  for(const args of [['--assembly'],['--assembly',''],['--assembly','  '],['--unknown','file.dll'],['--assembly','file.dll','extra']])
    assert.throws(()=>checker.parseAssemblyArguments(args),/用法/);
});
