import { shallowMount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import VForm3WorkflowDesigner from './VForm3WorkflowDesigner.vue';

const schema = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'main',
    fields: [{ fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} }]
  }]
} satisfies WorkflowFormSchema;
const catalog = {
  catalogVersion: 1,
  schemaVersion: 1,
  adapterVersion: 1,
  components: [{
    fieldTypeKey: 'text',
    designable: true,
    publishable: true,
    executable: true,
    constraintKeys: []
  }]
};

describe('VForm3WorkflowDesigner', () => {
  it('通过独立表单设计器 Host 接入 Workflow，而不直接拥有 VForm3 生命周期', () => {
    const wrapper = shallowMount(VForm3WorkflowDesigner, {
      props: { schema, catalog, disabled: true }
    });

    const host = wrapper.findComponent({ name: 'VForm3DesignerHost' });
    expect(host.exists()).toBe(true);
    expect(host.props('disabled')).toBe(true);
  });
});
