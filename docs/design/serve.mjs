// Serve only documentation assets on loopback, without exposing repository files.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, extname, resolve, relative, isAbsolute, sep } from 'node:path';

const root = dirname(fileURLToPath(import.meta.url));
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.md': 'text/plain; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = createServer(async (request, response) => {
  try {
    if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405); response.end(); return; }
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const file = resolve(root, `.${pathname === '/' ? '/wallets.html' : pathname}`);
    const rel = relative(root, file);
    if (isAbsolute(rel) || rel === '..' || rel.startsWith(`..${sep}`) || !types[extname(file)]) { response.writeHead(404); response.end(); return; }
    const bytes = await readFile(file);
    response.writeHead(200, { 'Content-Type': types[extname(file)], 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
    response.end(request.method === 'HEAD' ? undefined : bytes);
  } catch { response.writeHead(404); response.end(); }
});
server.listen(4319, '127.0.0.1', () => console.log('UI kit: http://127.0.0.1:4319/wallets.html'));
