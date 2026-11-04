import { EventEmitter } from 'node:events';
import { writeFileSync } from 'node:fs';
import { registerHooks } from 'node:module';

const target = new URL('../../e2e/admin-real-stack/scripts/bootstrap-stack.mjs', import.meta.url).href;
const calls = [];
// 只替代外部 SDK 边界，真实 bootstrap 仍负责生成构建参数和待运行程序集路径。
globalThis.__fullnetStackBuildProbe = { calls, EventEmitter, startedContainers: 0 };
const asModule = source => 'data:text/javascript,' + encodeURIComponent(source);
const sdk = asModule(`
  let sequence = 0;
  export class GenericContainer {
    constructor() {
      const id = (++sequence).toString(16).padStart(64, '0');
      let proxy;
      proxy = new Proxy(this, {get(_, key) {
        if (key === 'then') return undefined;
        if (key === 'start') return async () => {
          globalThis.__fullnetStackBuildProbe.startedContainers++;
          return proxy;
        };
        if (key === 'stop') return async () => {};
        if (key === 'exec') return async () => ({exitCode: 0, output: ''});
        if (key === 'getHost') return () => '127.0.0.1';
        if (key === 'getMappedPort') return port => port + 10000;
        if (key === 'getId') return () => id;
        return () => proxy;
      }});
      return proxy;
    }
  }
  export const Wait = new Proxy({}, {get: () => () => ({})});
`);
const childProcess = asModule(`
  export function execFileSync() { throw new Error('Unexpected synchronous subprocess in build probe'); }
  export function spawn(program, args) {
    const state = globalThis.__fullnetStackBuildProbe;
    state.calls.push({program, args});
    const child = new state.EventEmitter();
    const code = state.calls.length === 1 ? 0 : 43;
    queueMicrotask(() => { child.exitCode = code; child.emit('exit', code, null); });
    return child;
  }
`);
registerHooks({resolve(specifier, context, next) {
  if (specifier === 'testcontainers') return {url: sdk, shortCircuit: true};
  if (specifier === 'node:child_process' && context.parentURL === target)
    return {url: childProcess, shortCircuit: true};
  return next(specifier, context);
}});
try {
  const { bootstrapStack } = await import(target);
  await bootstrapStack();
  throw new Error('Probe must stop before starting a real Host');
} catch (error) {
  writeFileSync(process.env.FULLNET_STACK_BUILD_PROBE_REPORT,
    JSON.stringify({calls, error: error.message,
      startedContainers: globalThis.__fullnetStackBuildProbe.startedContainers,
      realDockerOrDotnetExecuted: false}, null, 2));
  if (calls.length !== 2 || !error.message.includes('43')) throw error;
}
