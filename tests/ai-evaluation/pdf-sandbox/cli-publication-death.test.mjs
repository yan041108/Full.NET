import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import test from 'node:test';

const cli = new URL('../../../scripts/ai/pdf-sandbox-cli.mjs', import.meta.url).href;
const source = String.raw`
import childProcess from 'node:child_process'; import {syncBuiltinESMExports} from 'node:module';
const original=childProcess.spawn;
childProcess.spawn=(...args)=>{const child=original(...args);process.stderr.write('writer-pid:'+child.pid+'\n');return child;};
syncBuiltinESMExports();
const {publishPdfSandboxProcess}=await import(process.argv[1]);
await publishPdfSandboxProcess(Buffer.alloc(1048576),{timeoutMs:30000});
`;
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

test('closing inherited stdout after controller death releases the owned blocked writer', async () => {
  const child = spawn(process.execPath, ['--input-type=module', '-e', source, cli], { windowsHide: true });
  let diagnostics = '', writerPid;
  child.stderr.on('data', bytes => {
    diagnostics += bytes.toString('utf8');
    writerPid = Number(/writer-pid:(\d+)\n/.exec(diagnostics)?.[1]) || undefined;
  });
  const exited = new Promise(resolve => child.once('exit', resolve));
  try {
    for (let attempt = 0; attempt < 100 && (!writerPid || !child.stdout.readableLength); attempt++) await delay(20);
    assert.ok(writerPid); assert.ok(child.stdout.readableLength, '实际写入开始后再终止控制器');
    assert.equal(child.exitCode, null);
    child.kill('SIGKILL'); await exited;
    // 先处理进程死亡再关闭管道，不等被辅助进程继承的 stdout EOF 或 child close。
    child.stdout.destroy();
    let writerGone = false;
    for (let attempt = 0; attempt < 100; attempt++) {
      try { process.kill(writerPid, 0); }
      catch (error) { if (error.code === 'ESRCH') { writerGone = true; break; } throw error; }
      await delay(20);
    }
    assert.equal(writerGone, true, '关闭读取端后辅助进程必须退出');
  } finally {
    child.kill('SIGKILL'); child.stdin.destroy(); child.stdout.destroy();
  }
});
