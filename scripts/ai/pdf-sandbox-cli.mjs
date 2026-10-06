import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { fileURLToPath } from 'node:url';
import { runPdfSandbox } from './pdf-sandbox.mjs';

const maxInput = 5 * 1024 * 1024;
const maxOutput = 2 * 1024 * 1024 * 6 + 128 * 96 + 512;
const failure = code => new Error(`fn_ai_pdf_sandbox_${code}`);

function argumentsOf(args) {
  const allowed = new Set(['--image-id', '--content-hash', '--size-bytes', '--timeout-ms', '--run-id', '--input-mode']);
  const values = new Map();
  for (let index = 0; index < args.length; index += 2) {
    if (!allowed.has(args[index]) || values.has(args[index]) || typeof args[index + 1] !== 'string') throw failure('invalid_input');
    values.set(args[index], args[index + 1]);
  }
  const imageId = values.get('--image-id');
  const contentHash = values.get('--content-hash');
  const size = values.get('--size-bytes');
  const timeout = values.get('--timeout-ms') ?? '30000';
  const runId = values.get('--run-id');
  const inputMode = values.get('--input-mode') ?? 'eof-v1';
  if (!/^sha256:[0-9a-f]{64}$/.test(imageId ?? '') || !/^[0-9a-f]{64}$/.test(contentHash ?? '')
      || !/^[1-9][0-9]{0,6}$/.test(size ?? '') || Number(size) > maxInput
      || !/^[1-9][0-9]{0,4}$/.test(timeout) || Number(timeout) > 30000
      || (runId !== undefined && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(runId))
      || !['eof-v1', 'framed-v1'].includes(inputMode)) throw failure('invalid_input');
  return { imageId, contentHash, sizeBytes: Number(size), timeoutMs: Number(timeout), runId, inputMode };
}

/** 帧模式以权威长度结束正文；管道必须保持打开到输出交付完成，EOF、错误或多余字节均表示取消。 */
function monitorFramedInput(input, sizeBytes, cancel) {
  let length = 0;
  const onData = bytes => {
    if (!Buffer.isBuffer(bytes)) { cancel(); return; }
    length += bytes.length;
    if (length > sizeBytes) cancel();
  };
  input.on('data', onData); input.on('end', cancel); input.on('error', cancel); input.on('close', cancel);
  return () => {
    input.removeListener('data', onData); input.removeListener('end', cancel);
    input.removeListener('error', cancel); input.removeListener('close', cancel);
  };
}

function readInput(input, signal, abortCode, exactSize) {
  return new Promise((resolve, reject) => {
    const chunks = []; let length = 0, finished = false;
    const finish = (error, bytes) => {
      if (finished) return;
      finished = true;
      if (error || exactSize === undefined) input.pause();
      input.removeListener('data', onData); input.removeListener('end', onEnd);
      input.removeListener('error', onError); input.removeListener('close', onClose);
      signal.removeEventListener('abort', onAbort);
      if (error) reject(error); else resolve(bytes);
    };
    const onData = bytes => {
      if (finished) return;
      // 只接受原始 Buffer；先检查累计大小，再保留分片，避免先收集无限 stdin。
      if (!Buffer.isBuffer(bytes)) { finish(failure('invalid_input')); return; }
      length += bytes.length;
      if (length > maxInput) finish(failure('input_limit'));
      else if (exactSize !== undefined && length > exactSize) finish(failure('cancelled'));
      else {
        chunks.push(bytes);
        if (length === exactSize) finish(null, Buffer.concat(chunks, length));
      }
    };
    const onEnd = () => finish(null, Buffer.concat(chunks, length));
    const onError = () => finish(failure('transport_failed'));
    const onClose = () => finish(failure('transport_failed'));
    const onAbort = () => finish(failure(abortCode()));
    input.on('data', onData); input.once('end', onEnd); input.once('error', onError); input.once('close', onClose);
    signal.addEventListener('abort', onAbort, { once: true });
    if (signal.aborted) onAbort();
  });
}

/** 本地进程传输边界：调用方提供权威来源指纹；成功结果仍须由业务 PDF 输出读取器校验。 */
export async function executePdfSandboxCli(args, input, { signal, runSandbox = runPdfSandbox } = {}) {
  let timer, onCancel, stopMonitoring;
  const controller = new AbortController(); let abortCode = 'cancelled';
  try {
    const options = argumentsOf(args);
    const deadline = performance.now() + options.timeoutMs;
    onCancel = () => controller.abort();
    signal?.addEventListener('abort', onCancel, { once: true });
    if (signal?.aborted) controller.abort();
    if (options.inputMode === 'framed-v1') stopMonitoring = monitorFramedInput(input, options.sizeBytes, onCancel);
    timer = setTimeout(() => {
      // 停止与清理可能继续等待；先发生的取消原因不能被后来的预算到期覆盖。
      if (!controller.signal.aborted) { abortCode = 'timeout'; controller.abort(); }
    }, options.timeoutMs);
    const bytes = await readInput(input, controller.signal, () => abortCode,
      options.inputMode === 'framed-v1' ? options.sizeBytes : undefined);
    if (bytes.length !== options.sizeBytes || createHash('sha256').update(bytes).digest('hex') !== options.contentHash)
      throw failure('source_mismatch');
    if (controller.signal.aborted || performance.now() >= deadline) throw failure(controller.signal.aborted ? abortCode : 'timeout');
    // 读取消耗同一预算；取消只通知已有控制器，等待其确认停止与清理，绝不只杀 Docker 客户端。
    const result = await runSandbox({ imageId: options.imageId, input: bytes, signal: controller.signal, runId: options.runId,
      timeoutMs: Math.max(1, Math.floor(deadline - performance.now())) });
    if (result.code !== 'ok') {
      if (result.code === 'fn_ai_pdf_sandbox_cancelled' && abortCode === 'timeout') throw failure('timeout');
      throw new Error(/^fn_ai_pdf_[a-z_]+$/.test(result.code ?? '') ? result.code : 'fn_ai_pdf_sandbox_transport_failed');
    }
    if (controller.signal.aborted || performance.now() >= deadline) throw failure(controller.signal.aborted ? abortCode : 'timeout');
    // 输出缓存只有在所有资源门禁完成后才能交接；清理失败不得泄漏已解析的部分或完整正文。
    if (result.stopped !== true || result.cleaned !== true || result.policyVerified !== true || result.exitCode !== 0
        || result.errorBytes !== 0 || !Buffer.isBuffer(result.output) || result.output.length === 0 || result.output.length > maxOutput
        || result.outputBytes !== result.output.length)
      throw failure('transport_failed');
    return { code: 'ok', output: result.output, remainingMs: Math.max(1, Math.floor(deadline - performance.now())) };
  } catch (error) {
    return { code: /^fn_ai_pdf_[a-z_]+$/.test(error.message ?? '') ? error.message : 'fn_ai_pdf_sandbox_transport_failed' };
  } finally {
    clearTimeout(timer);
    stopMonitoring?.();
    if (onCancel) signal?.removeEventListener('abort', onCancel);
  }
}

/** 交付输出也使用剩余时限；背压或取消关闭管道，已收到部分字节的消费者须按非零退出丢弃。 */
export function publishPdfSandboxOutput(destination, bytes, { signal, timeoutMs }) {
  return new Promise(resolve => {
    let completed = false, written = false;
    const finish = code => {
      if (completed) return;
      completed = true; clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
      destination.removeListener('close', onClose);
      // destroy 不携带正文或异常；保留错误监听直至 close，避免迟到的管道错误成为未捕获异常。
      if (code !== 'ok') destination.destroy();
      else destination.removeListener('error', onError);
      resolve(code);
    };
    const onError = () => finish('fn_ai_pdf_sandbox_transport_failed');
    const onClose = () => finish(signal?.aborted ? 'fn_ai_pdf_sandbox_cancelled' : written ? 'ok' : 'fn_ai_pdf_sandbox_transport_failed');
    const onAbort = () => finish('fn_ai_pdf_sandbox_cancelled');
    const timer = setTimeout(() => finish('fn_ai_pdf_sandbox_timeout'), timeoutMs);
    destination.on('error', onError); destination.once('close', onClose);
    destination.once('close', () => destination.removeListener('error', onError));
    signal?.addEventListener('abort', onAbort, { once: true });
    if (signal?.aborted) { onAbort(); return; }
    try {
      destination.end(bytes, error => {
        if (error) { onError(); return; }
        if (signal?.aborted) { onAbort(); return; }
        // end 回调只表示写入完成，关闭仍可能报错；等待 close 后才接受终端成功。
        written = true; destination.destroy();
      });
    }
    catch { onError(); }
  });
}

/** 固定辅助进程拥有潜在阻塞的 stdout 写入；控制进程先确认其退出，再报告成功或固定失败。 */
export function publishPdfSandboxProcess(bytes, { signal, timeoutMs }) {
  return new Promise(resolve => {
    let reason, finished = false;
    const child = spawn(process.execPath, [fileURLToPath(new URL('./pdf-sandbox-publish.mjs', import.meta.url))], {
      windowsHide: true, env: {}, stdio: ['pipe', 'inherit', 'ignore']
    });
    const stop = code => {
      if (reason) return;
      reason = code;
      // 只终止本次启动的辅助进程；stdout 已可能含部分字节，非零终态必须让调用方全部丢弃。
      child.kill('SIGKILL'); child.stdin.destroy();
    };
    const onAbort = () => stop('fn_ai_pdf_sandbox_cancelled');
    const timer = setTimeout(() => stop('fn_ai_pdf_sandbox_timeout'), timeoutMs);
    signal?.addEventListener('abort', onAbort, { once: true });
    child.stdin.on('finish', () => { finished = true; });
    child.stdin.on('error', () => stop('fn_ai_pdf_sandbox_transport_failed'));
    child.on('error', () => stop('fn_ai_pdf_sandbox_transport_failed'));
    child.once('close', code => {
      clearTimeout(timer); signal?.removeEventListener('abort', onAbort);
      resolve(reason ?? (signal?.aborted ? 'fn_ai_pdf_sandbox_cancelled' : code === 0 && finished ? 'ok' : 'fn_ai_pdf_sandbox_transport_failed'));
    });
    if (signal?.aborted) onAbort(); else child.stdin.end(bytes);
  });
}

async function main() {
  const controller = new AbortController();
  let stopMonitoring;
  const cancel = () => controller.abort();
  process.on('SIGINT', cancel); process.on('SIGTERM', cancel);
  try {
    const options = argumentsOf(process.argv.slice(2));
    // 主进程继续监测到辅助写入进程退出，避免解析成功后关闭 stdin 却仍交付正文。
    if (options.inputMode === 'framed-v1') stopMonitoring = monitorFramedInput(process.stdin, options.sizeBytes, cancel);
    const result = await executePdfSandboxCli(process.argv.slice(2), process.stdin, { signal: controller.signal });
    if (result.code !== 'ok' || controller.signal.aborted) {
      process.stderr.write(`${result.code === 'ok' ? 'fn_ai_pdf_sandbox_cancelled' : result.code}\n`);
      process.exitCode = 2;
    } else {
      // 消费方必须等到完整 EOF 和退出 0；管道写入失败或终止不能接受此前已收到的字节。
      // Windows 同步 stdio 和挂起的文件写入都能阻塞退出；写入隔离到可强制终止的固定辅助进程。
      let code = await publishPdfSandboxProcess(result.output, {
        signal: controller.signal, timeoutMs: result.remainingMs
      });
      if (code === 'ok' && controller.signal.aborted) code = 'fn_ai_pdf_sandbox_cancelled';
      if (code !== 'ok') {
        process.stderr.write(`${code}\n`);
      }
      process.exitCode = code === 'ok' ? 0 : 2;
    }
  } catch (error) {
    process.stderr.write(`${/^fn_ai_pdf_[a-z_]+$/.test(error.message ?? '') ? error.message : 'fn_ai_pdf_sandbox_transport_failed'}\n`);
    process.exitCode = 2;
  } finally {
    stopMonitoring?.();
    process.stdin.destroy();
    process.removeListener('SIGINT', cancel); process.removeListener('SIGTERM', cancel);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) await main();
