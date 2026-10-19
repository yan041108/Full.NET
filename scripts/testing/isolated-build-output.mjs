import { mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { setTimeout as delay } from 'node:timers/promises';

const exec = promisify(execFile);
const ownerFile = '.fullnet-build-output.json';
const key = value => process.platform === 'win32' ? value.toLowerCase() : value;
const contains = (parent, child) => {
  const relative = path.relative(key(parent), key(child));
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
};

/** 只为原生 dotnet build/publish 注入全局输出布局，其他任意命令不能借此跳过工作区锁。 */
export async function prepareIsolatedBuild({ cwd, output, command, args }) {
  if (!/^dotnet(?:\.exe)?$/iu.test(path.basename(command)) || !['build', 'publish'].includes(args[0])) {
    throw new Error('--isolated-output 只支持直接执行 dotnet build 或 dotnet publish。');
  }
  // 响应文件和覆盖输出/导入规则的参数可能让部分 ProjectReference 写回共享 obj，必须明确拒绝。
  const forbidden = /(?:UseArtifactsOutput|ArtifactsPath|ArtifactsProjectName|BaseOutputPath|OutputPath|BaseIntermediateOutputPath|IntermediateOutputPath|MSBuildProjectExtensionsPath|PublishDir|DirectoryBuildPropsPath|DirectoryBuildTargetsPath|CustomBefore|CustomAfter)/iu;
  for (const arg of args) {
    if (arg.startsWith('@') || arg.includes('@') && arg.startsWith('-')
      || /^(?:-o(?:=|:|$)|--output(?:=|$)|--artifacts-path(?:=|$))/iu.test(arg)
      || forbidden.test(arg) || /(?:^|[;:])(?:OutDir|IntDir)=/iu.test(arg)) {
      throw new Error('隔离构建不能另外覆盖输出路径、导入规则或使用响应文件。');
    }
  }
  let source = await realpath(cwd);
  try {
    const { stdout } = await exec('git', ['rev-parse', '--show-toplevel'], { cwd: source,
      windowsHide: true, timeout: 5000, maxBuffer: 4096 });
    source = await realpath(stdout.trim());
  } catch (error) { if (error.code !== 128 && error.code !== 'ENOENT') throw error; }
  const requested = path.resolve(output);
  if (/[,;\r\n]/u.test(requested)) throw new Error('隔离输出路径不能包含 MSBuild 属性分隔符。');
  if (contains(source, requested) || contains(requested, source)) throw new Error('隔离输出必须位于源码工作区之外。');
  await mkdir(requested, { recursive: true });
  const directory = await realpath(requested);
  if (contains(source, directory) || contains(directory, source)) throw new Error('隔离输出不能通过链接指向源码工作区。');
  const marker = path.join(directory, ownerFile);
  const expected = { schemaVersion: 1, source: key(source), output: key(directory), layout: 'dotnet-artifacts' };
  async function readOwner() {
    for (let attempt = 0; ; attempt += 1) {
      try { return JSON.parse(await readFile(marker, 'utf8')); }
      catch (error) {
        // 并发初始化同一输出时只等待归属文件写完，不抢占或替换别人的记录。
        if (!(error instanceof SyntaxError) || attempt >= 49) throw error;
        await delay(20);
      }
    }
  }
  let record;
  try { record = await readOwner(); }
  catch (error) {
    if (error.code !== 'ENOENT') throw error;
    if ((await readdir(directory)).some(name => name !== ownerFile)) throw new Error('隔离目录非空且无归属记录，不能占用。');
    try { await writeFile(marker, JSON.stringify(expected) + '\n', { flag: 'wx' }); }
    catch (writeError) { if (writeError.code !== 'EEXIST') throw writeError; }
    record = await readOwner();
  }
  if (JSON.stringify(record) !== JSON.stringify(expected)) throw new Error('隔离输出目录已属于其他源码或布局。');
  return { directory, args: [...args, '-p:UseArtifactsOutput=true', `-p:ArtifactsPath=${directory}`] };
}
