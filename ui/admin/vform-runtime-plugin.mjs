import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { readFile, stat } from 'node:fs/promises';
import { buildRuntimeOutput, verifyRuntimeRelease } from '../../packages/admin-form-designer/runtime-release.mjs';
const configFile = fileURLToPath(new URL('../../packages/admin-form-designer/vite.runtime.config.mjs', import.meta.url));
const runtimeOutput = fileURLToPath(new URL('../../packages/admin-form-designer/dist/runtime', import.meta.url));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.txt': 'text/plain', '.png': 'image/png', '.svg': 'image/svg+xml', '.ttf': 'font/ttf' };
async function buildRuntime(outDir) {
  await buildRuntimeOutput(configFile, outDir);
  await verifyRuntimeRelease(outDir);
}
// 仅专用静态目录允许 opaque Origin 读取，不把此策略扩展到业务 API。
function serveRuntime(directory) {
  return async (req, res, next) => {
    if (!req.url?.startsWith('/vform3/')) return next();
    const pathname = new URL(req.url, 'http://runtime.local').pathname;
    const file = path.resolve(directory, '.' + pathname.slice('/vform3'.length));
    if (!file.startsWith(directory + path.sep)) { res.statusCode = 404; res.end(); return; }
    try {
      if (!(await stat(file)).isFile()) throw new Error('not-file');
      res.setHeader('Content-Type', mime[path.extname(file)] ?? 'application/octet-stream');
      res.setHeader('Access-Control-Allow-Origin', '*'); res.setHeader('Cross-Origin-Resource-Policy', 'cross-origin');
      res.setHeader('Cache-Control', 'no-store'); res.end(await readFile(file));
    } catch { res.statusCode = 404; res.end(); }
  };
}
// 开发与正式包使用同一隔离产物；避免把 Vite HMR/源码服务器权限带进执行设计脚本的帧。
export function vformRuntimePlugin() {
  let output;
  return { name: 'fullnet-vform-runtime',
    configResolved(config) { output = path.resolve(config.root, config.build.outDir, 'vform3'); },
    async configureServer(server) {
      // 每个开发端口持有自己的产物，避免并行浏览器栈覆盖同一个输出目录。
      const servedOutput = path.join(path.dirname(runtimeOutput), 'runtime-' + server.config.server.port);
      await buildRuntime(servedOutput);
      server.middlewares.use(serveRuntime(servedOutput));
    },
    async configurePreviewServer(server) { await verifyRuntimeRelease(output); server.middlewares.use(serveRuntime(output)); },
    async closeBundle() { if (!this.meta.watchMode && output) await buildRuntime(output); }
  };
}
