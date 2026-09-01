/** 为本地冻结的 workflow-vue3 vendor 组件补最小声明，避免把第三方源码直接改成 TS。 */
declare module '*workflow-vue3/src/components/nodeWrap.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>;
  export default component;
}

/** 仅暴露当前适配层真实使用的 store 接口，未知能力保持未声明以避免误用。 */
declare module '*workflow-vue3/src/stores/index.js' {
  export function useStore(): {
    setFlowNodeConfig: (value: unknown) => void;
  };
}
