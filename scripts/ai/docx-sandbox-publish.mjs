import { publishDocxSandboxOutput } from './docx-sandbox-cli.mjs';

// 固定辅助进程只搬运有界结果，不把结果解释为命令；取消和硬时限由拥有该进程的控制器强制执行。
const maxOutput = 16 * 1024 * 1024;
try {
  if (process.argv.length !== 2) throw new Error('invalid arguments');
  const chunks = []; let length = 0;
  for await (const bytes of process.stdin) {
    length += bytes.length;
    if (length > maxOutput) throw new Error('output limit');
    chunks.push(bytes);
  }
  if (!length) throw new Error('empty output');
  const code = await publishDocxSandboxOutput(process.stdout, Buffer.concat(chunks, length), { timeoutMs: 30000 });
  process.exitCode = code === 'ok' ? 0 : 2;
} catch { process.exitCode = 2; }
