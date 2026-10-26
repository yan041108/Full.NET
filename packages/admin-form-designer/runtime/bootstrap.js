// 沙箱不获得同源权限；上游语言与撤销备份只使用当前 iframe 的内存存储。
const storage = new Map();
Object.defineProperty(window, 'localStorage', { value: {
  getItem: key => storage.get(String(key)) ?? null,
  setItem: (key, value) => storage.set(String(key), String(value)),
  removeItem: key => storage.delete(String(key)), clear: () => storage.clear(),
  key: index => [...storage.keys()][index] ?? null, get length() { return storage.size; }
} });
// 在载入任何可执行设计前，将同源资源进一步限制到专用静态路径。
const policy = document.createElement('meta'); policy.httpEquiv = 'Content-Security-Policy';
const assets = new URL('/vform3/', location.href).href;
policy.content = `connect-src ${assets}; script-src ${assets} 'unsafe-eval' 'unsafe-inline'; img-src ${assets} data: blob:; font-src ${assets} data:; frame-src 'none'; form-action 'none'`;
document.head.append(policy);
import('./main.js');
