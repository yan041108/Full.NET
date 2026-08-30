import { describe, expect, it } from 'vitest';
import { isWorkflowTodoDetail } from '../src/workflow-todos';

const validDetail = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  instanceId: '01912345-6789-7abc-8def-0123456789ac',
  stepId: '01912345-6789-7abc-8def-0123456789ad',
  assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
  statusKey: 'pending',
  revision: 3,
  formVersionId: '01912345-6789-7abc-8def-0123456789af',
  formSchema: {
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
  },
  submission: { summary: '采购审批' },
  fieldPolicies: { summary: 'editable' },
  submissionRevision: 4
};

describe('workflow todo runtime guards', () => {
  it('只接受受支持的静态表单协议和字段策略', () => {
    expect(isWorkflowTodoDetail(validDetail)).toBe(true);
    expect(isWorkflowTodoDetail({
      ...validDetail,
      fieldPolicies: { summary: 'execute-script' }
    })).toBe(false);
    expect(isWorkflowTodoDetail({
      ...validDetail,
      formSchema: {
        ...validDetail.formSchema,
        adapterVersion: 99
      }
    })).toBe(false);
  });
});
