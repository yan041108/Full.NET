declare module '*workflow-vue3/src/components/nodeWrap.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>;
  export default component;
}

declare module '*workflow-vue3/src/stores/index.js' {
  export function useStore(): {
    setFlowNodeConfig: (value: unknown) => void;
  };
}
