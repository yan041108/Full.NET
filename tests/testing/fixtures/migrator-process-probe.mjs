import { spawn } from 'node:child_process';
import { writeFileSync } from 'node:fs';

const [mode, ready] = process.argv.slice(2);
if (mode === 'child') {
  setInterval(() => {}, 1000);
} else if (mode === 'cancel') {
  const child = spawn(process.execPath, [import.meta.filename, 'child'], { stdio: 'inherit' });
  child.on('spawn', () => writeFileSync(ready, JSON.stringify({ pid: process.pid, childPid: child.pid })));
  setInterval(() => {}, 1000);
} else {
  writeFileSync(ready, JSON.stringify({ pid: process.pid }));
  if (mode === 'flood') {
    // 先填满 stderr 再输出 stdout，可复现依次读取两个管道时的互相等待。
    await new Promise(resolve => process.stderr.write('e'.repeat(1024 * 1024), resolve));
    process.stdout.write('completed');
  } else if (mode === 'fail') {
    process.stdout.write('migration-stdout');
    process.stderr.write('migration-stderr');
    process.exitCode = 23;
  }
}
