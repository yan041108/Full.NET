import { describe, expect, it } from 'vitest';
import {
  addWorkflowFormField,
  addWorkflowFormSection,
  createWorkflowFormDraft,
  removeWorkflowFormField,
  updateWorkflowFormField,
  type WorkflowFormComponentCatalogResponse
} from '../src/index.js';

const catalog: WorkflowFormComponentCatalogResponse = {
  catalogVersion: 1,
  schemaVersion: 1,
  adapterVersion: 1,
  components: [
    component('text', ['minLength', 'maxLength']),
    component('money', ['scale', 'minimum', 'maximum']),
    component('select', ['options']),
    component('switch', []),
    component('rich-editor', [], false)
  ]
};

describe('workflow form draft', () => {
  it('创建可由服务端编译的最小安全草稿', () => {
    expect(createWorkflowFormDraft()).toEqual({
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [{
        sectionKey: 'main',
        fields: [{
          fieldKey: 'summary',
          fieldTypeKey: 'text',
          required: true,
          constraints: {}
        }]
      }]
    });
  });

  it('按服务端目录添加字段并保持输入不可变', () => {
    const original = createWorkflowFormDraft();
    const withMoney = addWorkflowFormField(original, 'main', 'amount', 'money', catalog);
    const withChoice = addWorkflowFormField(withMoney, 'main', 'category', 'select', catalog);

    expect(original.sections[0]?.fields).toHaveLength(1);
    expect(withChoice.sections[0]?.fields.slice(1)).toEqual([
      {
        fieldKey: 'amount',
        fieldTypeKey: 'money',
        required: false,
        constraints: { scale: 2 }
      },
      {
        fieldKey: 'category',
        fieldTypeKey: 'select',
        required: false,
        constraints: { options: ['option1'] }
      }
    ]);
  });

  it.each([
    ['目录未开放字段', () => addWorkflowFormField(
      createWorkflowFormDraft(), 'main', 'content', 'rich-editor' as 'text', catalog)],
    ['重复字段键', () => addWorkflowFormField(
      createWorkflowFormDraft(), 'main', 'summary', 'text', catalog)],
    ['原型污染字段键', () => addWorkflowFormField(
      createWorkflowFormDraft(), 'main', '__proto__', 'text', catalog)],
    ['目录协议不一致', () => addWorkflowFormField(
      createWorkflowFormDraft(), 'main', 'amount', 'money', { ...catalog, adapterVersion: 2 })]
  ])('拒绝%s', (_name, action) => {
    expect(action).toThrowError('client.invalid_workflow_form_draft');
  });

  it('更新字段时仅保留目录声明的约束', () => {
    const schema = addWorkflowFormField(
      createWorkflowFormDraft(),
      'main',
      'description',
      'text',
      catalog
    );

    const updated = updateWorkflowFormField(schema, 'description', {
      fieldKey: 'details',
      required: true,
      constraints: { minLength: 2, maxLength: 120, remoteUrl: 'https://example.test' }
    }, catalog);

    expect(updated.sections[0]?.fields[1]).toEqual({
      fieldKey: 'details',
      fieldTypeKey: 'text',
      required: true,
      constraints: { minLength: 2, maxLength: 120 }
    });
    expect(schema.sections[0]?.fields[1]?.fieldKey).toBe('description');
  });

  it('添加 Section 并拒绝删除表单最后一个字段', () => {
    const schema = addWorkflowFormSection(createWorkflowFormDraft(), 'approval');

    expect(schema.sections[1]).toEqual({
      sectionKey: 'approval',
      fields: [{
        fieldKey: 'approval_field',
        fieldTypeKey: 'text',
        required: false,
        constraints: {}
      }]
    });
    expect(() => removeWorkflowFormField(createWorkflowFormDraft(), 'summary'))
      .toThrowError('client.invalid_workflow_form_draft');
    expect(removeWorkflowFormField(schema, 'approval_field').sections).toHaveLength(1);
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
