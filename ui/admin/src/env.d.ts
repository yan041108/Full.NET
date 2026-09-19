/// <reference types="vite/client" />

// 中文注释：为未走 vue-tsc 的 tsserver 提供 .vue 模块解析；具体组件类型仍由 vue-tsc 从 SFC 推导。
declare module '*.vue' {
  import type { DefineComponent } from 'vue';

  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>;
  export default component;
}