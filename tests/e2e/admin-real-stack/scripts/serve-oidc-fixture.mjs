import { createReadStream, existsSync, statSync } from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const port = Number.parseInt(process.argv[2] ?? '', 10);
const root = path.resolve(process.argv[3] ?? '.');
if (!Number.isFinite(port) || port <= 0) {
  throw new Error('Usage: node serve-oidc-fixture.mjs <port> <directory>');
}

const contentTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8']
]);

const server = http.createServer((request, response) => {
  const requestPath = request.url?.split('?', 1)[0] ?? '/';
  const relativePath = requestPath === '/' ? '/index.html' : requestPath;
  const filePath = path.join(root, relativePath);
  if (!filePath.startsWith(root) || !existsSync(filePath) || !statSync(filePath).isFile()) {
    response.writeHead(404);
    response.end('Not found');
    return;
  }

  const extension = path.extname(filePath).toLowerCase();
  response.writeHead(200, {
    'Content-Type': contentTypes.get(extension) ?? 'application/octet-stream',
    'Cache-Control': 'no-store'
  });
  createReadStream(filePath).pipe(response);
});

server.listen(port, 'localhost', () => {
  console.log(`OIDC fixture listening on http://localhost:${port}`);
});