import 'layui/dist/css/layui.css';
import '@fullnet/design-tokens/tokens.css';
import '../css/app.css';
import '../css/shell-layouts.css';
import { initializeAdminApp } from './app.js';
import { deferLayuiRuntime } from './core/layui-runtime.js';

// 页面入口只负责装配资源，业务交互保持在可独立测试的 app.js 中。
const app = initializeAdminApp(document);
deferLayuiRuntime({
  ready: app.ready,
  importRuntime: () => import('layui/dist/layui.js'),
  enhance: () => app.enhanceLayui(),
  onError: error => console.error('Layui runtime failed to load.', error)
});
