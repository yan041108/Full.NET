import { afterEach, beforeAll, expect, it, vi } from 'vitest';
import type { App } from 'vue';

vi.mock('./App.vue', async () => {
  const { defineComponent } = await import('vue');
  return { default: defineComponent({
    template: '<section class="startup-loading-probe" v-loading="true">加载中</section>'
  }) };
});

// 真实入口的模块初始化属于夹具准备，不占用遮罩行为的默认断言预算。
beforeAll(async () => {
  document.body.innerHTML = '<div id="app"></div>';
  await import('./main');
});

afterEach(() => {
  const root = document.getElementById('app') as (HTMLElement & { __vue_app__?: App }) | null;
  root?.__vue_app__?.unmount();
  document.body.innerHTML = '';
});

it('管理端入口为页面注册可运行的加载遮罩指令', async () => {
  await vi.waitFor(() => {
    expect(document.querySelector('.startup-loading-probe')).not.toBeNull();
    expect(document.querySelector('.startup-loading-probe .el-loading-mask')).not.toBeNull();
  });
});
