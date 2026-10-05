import { execFile, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { promisify } from 'node:util';

const execute = promisify(execFile);
const label = 'fullnet.ai.pdf.run';
const ownerLabel = 'fullnet.ai.pdf.owner';
const memory = 512 * 1024 * 1024;
const maxInput = 5 * 1024 * 1024;
const maxOutput = 2 * 1024 * 1024 * 6 + 128 * 96 + 512;
const maxErrors = 4096;
const environmentKeys = new Set(['PATH', 'APP_UID', 'ASPNETCORE_HTTP_PORTS', 'DOTNET_RUNNING_IN_CONTAINER']);
const failure = code => new Error(`fn_ai_pdf_sandbox_${code}`);

// Docker 控制客户端可使用宿主已有连接配置；没有任何 -e、挂载或凭据进入解析容器。
async function control(args, { missingAllowed = false, stoppedAllowed = false } = {}) {
  try {
    const result = await execute('docker', args, { windowsHide: true, timeout: 10000, maxBuffer: 65536 });
    return result.stdout;
  } catch (error) {
    if (missingAllowed && error.code === 1 && /No such (object|container)/i.test(error.stderr ?? '')) return null;
    if (stoppedAllowed && error.code === 1 && /container\b[^\r\n]*\bis not running\b/i.test(error.stderr ?? '')) return null;
    throw failure('control_failed');
  }
}

function validatePolicy(container, imageId, runId) {
  const host = container.HostConfig;
  const config = container.Config;
  if (container.Image !== imageId || config.Labels?.[label] !== runId
      || config.User !== '65532:65532' || host.NetworkMode !== 'none'
      || !host.ReadonlyRootfs || host.Privileged || host.Memory !== memory || host.MemorySwap !== memory
      || host.NanoCpus !== 2000000000 || host.PidsLimit !== 32 || host.IpcMode !== 'none'
      || host.LogConfig.Type !== 'none'
      || !host.CapDrop?.includes('ALL') || !host.SecurityOpt?.includes('no-new-privileges')
      || container.Mounts.length || Object.keys(host.PortBindings ?? {}).length
      || (host.Devices ?? []).length || Object.keys(config.Volumes ?? {}).length
      || config.Env.some(value => !environmentKeys.has(value.split('=', 1)[0]))
      || !host.Ulimits?.some(item => item.Name === 'nofile' && item.Soft === 64 && item.Hard === 64))
    throw failure('policy_rejected');
}

/** 本地独立执行边界；可信调用者提供验收后的不可变 image ID，业务导入仍需自己的授权和协议验收。 */
export async function runPdfSandbox({ imageId, input, signal, timeoutMs = 30000, runId = randomUUID() } = {}) {
  if (!/^sha256:[0-9a-f]{64}$/.test(imageId ?? '') || !Buffer.isBuffer(input)
      || input.length > maxInput || !Number.isInteger(timeoutMs) || timeoutMs <= 0 || timeoutMs > 30000
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(runId)
      || (signal !== undefined && !(signal instanceof AbortSignal))) throw failure('invalid_input');
  if (signal?.aborted) return { code: 'fn_ai_pdf_sandbox_cancelled' };
  // 关联 ID 可重复；资源所有权必须由每次调用内部生成，不能把另一任务当作本次资源恢复。
  const ownerId = randomUUID();
  const name = `fullnet-pdf-${ownerId}`;
  const started = performance.now();
  const deadline = started + timeoutMs;
  let containerId;
  let attemptedCreate = false;
  let result;
  let cleaned = false;

  async function inspectOwned(missingAllowed = false) {
    const json = await control(['container', 'inspect', containerId ?? name], { missingAllowed });
    if (json === null) return null;
    const container = JSON.parse(json)[0];
    if (!/^[0-9a-f]{64}$/.test(container.Id) || container.Config.Labels?.[label] !== runId
        || container.Config.Labels?.[ownerLabel] !== ownerId
        || container.Name !== `/${name}` || (containerId && container.Id !== containerId)) throw failure('ownership_rejected');
    containerId = container.Id;
    return container;
  }

  async function forceStop() {
    const container = await inspectOwned();
    // watchdog 可在 inspect 与 kill 之间先终止；仅识别已停止响应，随后仍复验归属及真实状态。
    if (container.State.Running) await control(['kill', '--signal', 'KILL', containerId], { stoppedAllowed: true });
    const stopped = await inspectOwned();
    if (stopped.State.Running) throw failure('stop_unconfirmed');
    return stopped;
  }

  try {
    const image = JSON.parse(await control(['image', 'inspect', imageId]))[0];
    if (image.Id !== imageId || Object.keys(image.Config.Volumes ?? {}).length
        || image.Config.Env?.some(value => !environmentKeys.has(value.split('=', 1)[0]))) throw failure('policy_rejected');
    if (signal?.aborted) return { code: 'fn_ai_pdf_sandbox_cancelled' };
    if (performance.now() >= deadline) return { code: 'fn_ai_pdf_sandbox_timeout' };
    const watchdogSeconds = ((deadline - performance.now()) / 1000).toFixed(3);
    attemptedCreate = true;
    const created = (await control(['create', '--name', name, '--label', `${label}=${runId}`, '--label', `${ownerLabel}=${ownerId}`,
      '--network', 'none', '--read-only', '--user', '65532:65532', '--cap-drop', 'ALL',
      '--security-opt', 'no-new-privileges', '--memory', '512m', '--memory-swap', '512m',
      '--cpus', '2', '--pids-limit', '32', '--ulimit', 'nofile=64:64', '--ipc', 'none',
      '--no-healthcheck', '--log-driver', 'none', '--interactive', '--entrypoint', '/usr/bin/timeout', imageId,
      '--signal=KILL', '--kill-after=1', watchdogSeconds, '/app/parser'])).trim();
    if (!/^[0-9a-f]{64}$/.test(created)) throw failure('control_failed');
    containerId = created;
    validatePolicy(await inspectOwned(), imageId, runId);

    const attached = await new Promise((resolve, reject) => {
      const child = spawn('docker', ['start', '--attach', '--interactive', containerId], { windowsHide: true });
      const chunks = [];
      const errors = [];
      let outputBytes = 0, errorBytes = 0;
      let reason;
      let stopping;
      let stdinFinished = false;
      // 一次且幂等地强杀，等待退出状态；不把杀掉 Docker 客户端等同于杀掉解析器。
      const stop = code => {
        if (reason) return;
        reason = code;
        stopping = forceStop().catch(() => { reason = 'fn_ai_pdf_sandbox_stop_unconfirmed'; })
          .finally(() => child.kill());
      };
      const onAbort = () => stop('fn_ai_pdf_sandbox_cancelled');
      const timer = setTimeout(() => stop('fn_ai_pdf_sandbox_timeout'), Math.max(1, deadline - performance.now()));
      signal?.addEventListener('abort', onAbort, { once: true });
      child.stdout.on('data', bytes => {
        outputBytes += bytes.length;
        if (outputBytes > maxOutput) stop('fn_ai_pdf_sandbox_output_limit');
        else if (!reason) chunks.push(bytes);
      });
      child.stderr.on('data', bytes => {
        errorBytes += bytes.length;
        if (errorBytes > maxErrors) stop('fn_ai_pdf_sandbox_error_limit');
        else if (!reason) errors.push(bytes);
      });
      child.stdin.on('finish', () => { stdinFinished = true; });
      // 解析器早期拒绝输入会关闭管道；只接受写入完整且 exit=0 的成功结果。
      child.stdin.on('error', () => { });
      child.on('error', () => stop('fn_ai_pdf_sandbox_transport_failed'));
      child.on('close', async exit => {
        clearTimeout(timer);
        signal?.removeEventListener('abort', onAbort);
        try {
          await stopping;
          resolve({ reason, exit, stdinFinished, outputBytes, errorBytes,
            output: reason ? undefined : Buffer.concat(chunks), errors: reason ? '' : Buffer.concat(errors).toString('utf8') });
        } catch { reject(failure('control_failed')); }
      });
      child.stdin.end(input);
      if (signal?.aborted) onAbort();
      if (performance.now() >= deadline) stop('fn_ai_pdf_sandbox_timeout');
    });
    const stopped = await forceStop();
    let code = attached.reason;
    if (!code && signal?.aborted) code = 'fn_ai_pdf_sandbox_cancelled';
    if (!code && stopped.State.OOMKilled) code = 'fn_ai_pdf_sandbox_memory_limit';
    if (!code && (stopped.State.ExitCode === 137 || stopped.State.ExitCode === 124)) code = 'fn_ai_pdf_sandbox_timeout';
    if (!code && (attached.exit !== 0 || stopped.State.ExitCode !== 0 || !attached.stdinFinished))
      code = /^fn_ai_pdf_[a-z_]+\r?\n$/.test(attached.errors) ? attached.errors.trim() : 'fn_ai_pdf_sandbox_parse_failed';
    if (!code && attached.errorBytes !== 0) code = 'fn_ai_pdf_sandbox_parse_failed';
    result = { code: code ?? 'ok', containerId, stopped: !stopped.State.Running,
      exitCode: stopped.State.ExitCode, oomKilled: stopped.State.OOMKilled,
      outputBytes: attached.outputBytes, errorBytes: attached.errorBytes,
      output: code ? undefined : attached.output, policyVerified: true };
  } catch (error) {
    result = { code: /^fn_ai_pdf_sandbox_[a-z_]+$/.test(error.message) ? error.message : 'fn_ai_pdf_sandbox_control_failed', containerId };
  } finally {
    if (attemptedCreate) {
      try {
        // create 的响应丢失时仍按唯一名称恢复身份；不清理名称相同但归属不符的资源。
        const container = await inspectOwned(true);
        if (container) {
          if (container.State.Running) await forceStop();
          await inspectOwned();
          await control(['rm', containerId]);
          if (await inspectOwned(true)) throw failure('cleanup_failed');
        }
        cleaned = true;
      } catch { result = { code: 'fn_ai_pdf_sandbox_cleanup_failed', containerId }; }
    }
  }
  return { ...result, cleaned, durationMs: Math.ceil(performance.now() - started) };
}
