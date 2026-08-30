import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import type {
  WorkflowFormComponentCatalogResponse,
  WorkflowFormSchema
} from '@fullnet/client-contracts';
import WorkflowFormDesigner from './WorkflowFormDesigner.vue';

const schema = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'request',
    fields: [{
      fieldKey: 'summary',
      fieldTypeKey: 'text',
      required: true,
      constraints: { maxLength: 120 }
    }]
  }]
} satisfies WorkflowFormSchema;

const catalog = {
  catalogVersion: 1,
  schemaVersion: 1,
  adapterVersion: 1,
  components: [
    component('text', ['minLength', 'maxLength']),
    component('money', ['scale', 'minimum', 'maximum']),
    component('select', ['options']),
    component('rich-editor', [], false)
  ]
} satisfies WorkflowFormComponentCatalogResponse;

describe('WorkflowFormDesigner', () => {
  it('只显示服务端允许的静态字段类型并添加安全字段', async () => {
    const wrapper = mount(WorkflowFormDesigner, {
      props: { schema, catalog, disabled: false }
    });

    const fieldType = wrapper.get('[data-testid="workflow-designer-new-field-type"]');
    expect(fieldType.text()).toContain('text');
    expect(fieldType.text()).toContain('money');
    expect(fieldType.text()).not.toContain('rich-editor');

    await wrapper.get('[data-testid="workflow-designer-new-field-key"]').setValue('amount');
    await fieldType.setValue('money');
    await wrapper.get('[data-testid="workflow-designer-add-field"]').trigger('click');

    const emitted = wrapper.emitted('update:schema')?.at(-1)?.[0] as WorkflowFormSchema;
    expect(emitted.sections[0]?.fields[1]).toEqual({
      fieldKey: 'amount',
      fieldTypeKey: 'money',
      required: false,
      constraints: { scale: 2 }
    });
  });

  it('通过显式控件编辑稳定键、必填与目录约束', async () => {
    const choiceSchema = {
      ...schema,
      sections: [{
        sectionKey: 'request',
        fields: [{
          fieldKey: 'category',
          fieldTypeKey: 'select',
          required: false,
          constraints: { options: ['hardware'] }
        }]
      }]
    } satisfies WorkflowFormSchema;
    const wrapper = mount(WorkflowFormDesigner, {
      props: { schema: choiceSchema, catalog, disabled: false }
    });

    await wrapper.get('[data-field-key="category"] [data-field-property="fieldKey"]')
      .setValue('purchaseCategory');
    await wrapper.get('[data-field-key="purchaseCategory"] [data-field-property="required"]')
      .setValue(true);
    await wrapper.get('[data-field-key="purchaseCategory"] [data-constraint-key="options"]')
      .setValue('hardware\nservice');

    const emitted = wrapper.emitted('update:schema')?.at(-1)?.[0] as WorkflowFormSchema;
    expect(emitted.sections[0]?.fields[0]).toEqual({
      fieldKey: 'purchaseCategory',
      fieldTypeKey: 'select',
      required: true,
      constraints: { options: ['hardware', 'service'] }
    });
  });

  it('支持添加 Section 和删除字段且错误保持可见', async () => {
    const wrapper = mount(WorkflowFormDesigner, {
      props: { schema, catalog, disabled: false }
    });

    await wrapper.get('[data-testid="workflow-designer-new-section-key"]').setValue('approval');
    await wrapper.get('[data-testid="workflow-designer-add-section"]').trigger('click');
    expect(wrapper.find('[data-section-key="approval"]').exists()).toBe(true);

    await wrapper.get('[data-field-key="approval_field"] [data-testid="workflow-designer-remove-field"]')
      .trigger('click');
    expect(wrapper.find('[data-section-key="approval"]').exists()).toBe(false);

    await wrapper.get('[data-field-key="summary"] [data-testid="workflow-designer-remove-field"]')
      .trigger('click');
    expect(wrapper.get('[role="alert"]').text()).toContain('client.invalid_workflow_form_draft');
  });

  it('只读模式不创建任何修改动作', () => {
    const wrapper = mount(WorkflowFormDesigner, {
      props: { schema, catalog, disabled: true }
    });

    expect(wrapper.find('[data-designer-action]').exists()).toBe(false);
    expect(wrapper.get('[data-field-key="summary"] input').attributes('readonly')).toBeDefined();
    expect(wrapper.html()).not.toContain('<script');
    expect(wrapper.html()).not.toContain('<iframe');
  });
});

function component(
  fieldTypeKey: string,
  constraintKeys: string[],
  enabled = true
) {
  return {
    fieldTypeKey,
    designable: enabled,
    publishable: enabled,
    executable: enabled,
    constraintKeys
  };
}
