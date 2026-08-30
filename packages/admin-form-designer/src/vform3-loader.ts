/// <reference path="./vform3-builds.d.ts" />

import type { App, Component } from 'vue';

const installations = new WeakMap<App, Promise<Component>>();

/**
 * VForm3 会向 Vue App 注册全局组件，必须按 App 隔离安装状态，避免多宿主或测试环境共享错误实例。
 */
export function loadVForm3Designer(app: App): Promise<Component> {
  const installed = installations.get(app);
  if (installed !== undefined) return installed;

  const installation = Promise.all([
    import('./element-plus-components'),
    import('vform3-builds'),
    import('vform3-builds/dist/designer.style.css')
  ]).then(([elementPlus, vform3]) => {
    // VForm3 通过全局 el-* 组件渲染设计器，安装顺序属于运行时契约，不能交给业务宿主隐式满足。
    elementPlus.installVFormElementPlusComponents(app);
    installVForm3WithoutAxiosLeak(app, vform3.default);
    const component = app.component('VFormDesigner');
    if (component === undefined) throw new Error('client.vform3_install_failed');
    return component;
  });
  installations.set(app, installation);
  return installation;
}

function installVForm3WithoutAxiosLeak(app: App, plugin: Parameters<App['use']>[0]): void {
  const previousAxios = Object.getOwnPropertyDescriptor(window, 'axios');
  try {
    app.use(plugin);
  } finally {
    // 上游安装器会把内置旧版 Axios 写入 window；恢复宿主描述符，避免跨模块全局污染。
    if (previousAxios === undefined) {
      delete (window as unknown as Record<string, unknown>).axios;
    } else {
      Object.defineProperty(window, 'axios', previousAxios);
    }
  }
}
