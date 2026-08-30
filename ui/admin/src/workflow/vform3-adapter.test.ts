import { describe, expect, it } from 'vitest';
import type {
  WorkflowFormComponentCatalogResponse,
  WorkflowFormSchema
} from '@fullnet/client-contracts';
import {
  fromVFormDesignerJson,
  toVFormDesignerJson
} from './vform3-adapter';

const catalog = {
  catalogVersion: 1,
  schemaVersion: 1,
  adapterVersion: 1,
  components: [
    component('text', ['minLength', 'maxLength']),
    component('decimal', ['scale', 'minimum', 'maximum']),
    component('select', ['options']),
    component('date', ['minimum', 'maximum'])
  ]
} satisfies WorkflowFormComponentCatalogResponse;

describe('VForm3 Workflow Schema 适配器', () => {
  it('把权威 Schema 转换成 VForm3 可编辑 JSON，并保留稳定字段与 Section 键', () => {
    const schema = {
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [{
        sectionKey: 'request',
        fields: [
          {
            fieldKey: 'summary',
            fieldTypeKey: 'text',
            required: true,
            constraints: { minLength: 2, maxLength: 120 }
          },
          {
            fieldKey: 'amount',
            fieldTypeKey: 'decimal',
            required: false,
            constraints: { scale: 2, minimum: 0 }
          }
        ]
      }]
    } satisfies WorkflowFormSchema;

    const value = toVFormDesignerJson(schema);

    expect(value.widgetList).toHaveLength(2);
    expect(value.widgetList[0]).toMatchObject({
      type: 'input',
      options: {
        name: 'summary',
        required: true,
        fullNetSectionKey: 'request',
        minLength: 2,
        maxLength: 120
      }
    });
    expect(value.widgetList[1]).toMatchObject({
      type: 'number',
      options: { name: 'amount', precision: 2, min: 0 }
    });
  });

  it('只把服务端目录允许的静态控件转换回权威 Schema', () => {
    const value = fromVFormDesignerJson({
      widgetList: [
        widget('input', 'summary', 'request', { required: true, maxLength: 80 }),
        widget('select', 'category', 'request', {
          optionItems: [{ label: '硬件', value: 'hardware' }, { label: '服务', value: 'service' }]
        }),
        widget('date', 'neededAt', 'schedule')
      ],
      formConfig: {}
    }, catalog);

    expect(value).toEqual({
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [
        {
          sectionKey: 'request',
          fields: [
            { fieldKey: 'summary', fieldTypeKey: 'text', required: true, constraints: { maxLength: 80 } },
            { fieldKey: 'category', fieldTypeKey: 'select', required: false, constraints: { options: ['hardware', 'service'] } }
          ]
        },
        {
          sectionKey: 'schedule',
          fields: [
            { fieldKey: 'neededAt', fieldTypeKey: 'date', required: false, constraints: {} }
          ]
        }
      ]
    });
  });

  it.each(['html-text', 'picture-upload', 'file-upload', 'rich-editor', 'custom']) (
    '拒绝危险或非目录控件 %s',
    type => {
      expect(() => fromVFormDesignerJson({
        widgetList: [widget(type, 'unsafe', 'request')],
        formConfig: {}
      }, catalog)).toThrow('client.unsupported_vform_component');
    }
  );

  it('拒绝脚本、远程资源与重复字段键', () => {
    expect(() => fromVFormDesignerJson({
      widgetList: [widget('input', 'summary', 'request')],
      formConfig: { onFormCreated: 'return globalThis.document' }
    }, catalog)).toThrow('client.unsafe_vform_configuration');

    expect(() => fromVFormDesignerJson({
      widgetList: [
        widget('input', 'summary', 'request'),
        widget('input', 'summary', 'other')
      ],
      formConfig: {}
    }, catalog)).toThrow('client.invalid_workflow_form_draft');
  });
});

function component(fieldTypeKey: string, constraintKeys: string[]) {
  return {
    fieldTypeKey,
    designable: true,
    publishable: true,
    executable: true,
    constraintKeys
  };
}

function widget(
  type: string,
  name: string,
  sectionKey: string,
  options: Record<string, unknown> = {}
) {
  return {
    id: `${type}-${name}`,
    type,
    options: {
      name,
      label: name,
      fullNetSectionKey: sectionKey,
      required: false,
      ...options
    }
  };
}
