import 'layui/dist/css/layui.css';
import 'layui/dist/layui.js';
import '@fullnet/design-tokens/tokens.css';
import '../css/app.css';
import '../css/shell-layouts.css';
import { initializeAdminApp } from './app.js';

// 页面入口只负责装配资源，业务交互保持在可独立测试的 app.js 中。
initializeAdminApp(document);
