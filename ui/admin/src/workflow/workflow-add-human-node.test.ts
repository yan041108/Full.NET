import { mount } from '@vue/test-utils';
import { computed } from 'vue';
import { describe, expect, it } from 'vitest';
import AddNode from './vendor/workflow-vue3/src/components/addNode.vue';

describe('人工节点真实添加入口', () => {
  for (const [kind, type, label, policy] of [
    ['human.vote', 19, '投票', 'votePolicy'], ['human.claim', 17, '抢单', 'claimPolicy']
  ] as const) {
    it(`${kind} 点击后实际插入节点并保留后继`, async () => {
      const next = { id: 'existing', type: 1, childNode: null };
      const wrapper = mount(AddNode, { props: { childNodeP: next }, global: {
        provide: { fullnetWorkflowEnabledNodeTypes: computed(() => new Set([kind])) },
        stubs: { ElPopover: { template: '<div><slot /><slot name="reference" /></div>' } }
      } });
      try {
        const button = wrapper.findAll('button').find(candidate => candidate.text().includes(label));
        expect(button).toBeDefined(); await button!.trigger('click');
        const inserted = wrapper.emitted('update:childNodeP')?.[0]?.[0] as Record<string, unknown>;
        expect(inserted.type).toBe(type); expect(inserted.childNode).toEqual(next); expect(inserted[policy]).toBeDefined();
      } finally { wrapper.unmount(); }
    });
  }
});
