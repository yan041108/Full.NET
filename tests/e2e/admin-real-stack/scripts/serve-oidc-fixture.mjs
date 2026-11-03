import { createReadStream, existsSync, statSync, readFileSync } from 'node:fs';
import http from 'node:http';
import https from 'node:https';
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

const handler = (request, response) => {
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
    'Referrer-Policy': 'no-referrer',
    'Cache-Control': 'no-store'
  });
  createReadStream(filePath).pipe(response);
};

const certificate = process.env.FULLNET_E2E_TLS_CERT;
const privateKey = process.env.FULLNET_E2E_TLS_KEY;
if (Boolean(certificate) !== Boolean(privateKey)) throw new Error('TLS certificate and key must be configured together.');
const server = certificate
  ? https.createServer({ cert: readFileSync(certificate), key: readFileSync(privateKey) }, handler)
  : http.createServer(handler);

server.listen(port, process.env.FULLNET_E2E_BIND_HOST ?? 'localhost', () => {
  console.log(`OIDC fixture listening on ${certificate ? 'https' : 'http'} port ${port}`);
});
