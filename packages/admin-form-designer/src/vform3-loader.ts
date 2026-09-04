import type { App, Component } from 'vue';

const installations = new WeakMap<App, Promise<Component>>();

/** 按 Vue App 隔离 ESM 组件加载状态，避免多宿主或测试环境共享错误 Promise。 */
export function loadVForm3Designer(app: App): Promise<Component> {
  const installed = installations.get(app);
  if (installed !== undefined) return installed;

  // 本地 ESM 安全子集由当前 Vue/Vite 直接编译，不注册全局组件，也不会污染 window。
  const installation = import('./esm/VForm3EsmDesigner.vue')
    .then(module => module.default as Component);
  installations.set(app, installation);
  return installation;
}
