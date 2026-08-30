import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import type { WorkflowFormSchema } from '@fullnet/client-contracts';
import WorkflowFormRenderer from './WorkflowFormRenderer.vue';

const schema = {
  schemaVersion: 1,
  adapterVersion: 1,
  sections: [{
    sectionKey: 'request',
    fields: [
      { fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: {} },
      { fieldKey: 'amount', fieldTypeKey: 'integer', required: false, constraints: {} },
      { fieldKey: 'secret', fieldTypeKey: 'text', required: false, constraints: {} }
    ]
  }]
} satisfies WorkflowFormSchema;

describe('WorkflowFormRenderer', () => {
  it('隐藏不可见字段、锁定只读字段并只发送可编辑 patch', async () => {
    const wrapper = mount(WorkflowFormRenderer, {
      props: {
        schema,
        submission: { summary: '原始摘要', amount: 12 },
        fieldPolicies: {
          summary: 'readOnly',
          amount: 'required',
          secret: 'hidden'
        }
      }
    });

    expect(wrapper.find('[data-field-key="secret"]').exists()).toBe(false);
    expect(wrapper.find('[data-field-key="summary"] input').attributes('readonly')).toBeDefined();

    await wrapper.find('[data-field-key="amount"] input').setValue('25');

    expect(wrapper.emitted('update:patch')?.at(-1)).toEqual([{ amount: 25 }]);
  });

  it('把发布版本的文本长度和整数范围投影为原生输入约束', () => {
    const constrainedSchema = {
      ...schema,
      sections: [{
        sectionKey: 'request',
        fields: [
          {
            fieldKey: 'summary',
            fieldTypeKey: 'text',
            required: true,
            constraints: { minLength: 2, maxLength: 64 }
          },
          {
            fieldKey: 'amount',
            fieldTypeKey: 'integer',
            required: false,
            constraints: { minimum: 1, maximum: 100 }
          }
        ]
      }]
    } satisfies WorkflowFormSchema;

    const wrapper = mount(WorkflowFormRenderer, {
      props: {
        schema: constrainedSchema,
        submission: {},
        fieldPolicies: {}
      }
    });

    expect(wrapper.get('[data-field-key="summary"] input').attributes()).toMatchObject({
      minlength: '2',
      maxlength: '64'
    });
    expect(wrapper.get('[data-field-key="amount"] input').attributes()).toMatchObject({
      min: '1',
      max: '100'
    });
  });
});
