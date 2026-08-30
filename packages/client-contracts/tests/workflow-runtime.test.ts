import { describe, expect, it } from 'vitest';
import { readWorkflowFormVersionSchema } from '../src/workflow-runtime';

const version = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  formDefinitionId: '01912345-6789-7abc-8def-0123456789ac',
  versionNumber: 1,
  schemaVersion: 1,
  adapterVersion: 1,
  componentCatalogVersion: 1,
  formSchemaJson: JSON.stringify({
    schemaVersion: 1,
    adapterVersion: 1,
    sections: [{
      sectionKey: 'request',
      fields: [{
        fieldKey: 'summary',
        fieldTypeKey: 'text',
        required: true,
        constraints: {}
      }]
    }]
  }),
  webRenderSchemaJson: '{}',
  contentHash: 'hash',
  publishedById: '01912345-6789-7abc-8def-0123456789ad',
  publishedAtUtc: '2026-08-30T00:00:00Z'
};

describe('workflow runtime contracts', () => {
  it('只解析受支持的已发布表单协议', () => {
    expect(readWorkflowFormVersionSchema(version).sections).toHaveLength(1);
    expect(() => readWorkflowFormVersionSchema({
      ...version,
      formSchemaJson: '{invalid'
    })).toThrow('client.invalid_workflow_form_schema');
    expect(() => readWorkflowFormVersionSchema({
      ...version,
      formSchemaJson: JSON.stringify({
        schemaVersion: 1,
        adapterVersion: 2,
        sections: []
      })
    })).toThrow('client.invalid_workflow_form_schema');
  });
});
