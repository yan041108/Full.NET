import { describe, expect, it } from 'vitest';
import { isWorkflowFormSchema, isWorkflowTodoDetail } from '../src/workflow-todos';
import goldenFixture from '../src/fixtures/workflow-form-schema-v1.json';

const validDetail = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  instanceId: '01912345-6789-7abc-8def-0123456789ac',
  stepId: '01912345-6789-7abc-8def-0123456789ad',
  assigneeUserId: '01912345-6789-7abc-8def-0123456789ae',
  statusKey: 'pending',
  revision: 3,
  formVersionId: '01912345-6789-7abc-8def-0123456789af',
  formSchemaHash: 'c067f7ee2860ffd2cc3e3f450af5d6988dc989537e468972d6e3804efddcd4cb',
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
  submissionRevision: 4,
  approvalModeKey: 'single',
  requiredApprovalCount: 1,
  approvedCount: 0,
  rejectedCount: 0,
  pendingCount: 1
};

describe('workflow todo runtime guards', () => {
  it('接受跨端共享的 v1 Golden Fixture', () => {
    expect(isWorkflowFormSchema(goldenFixture.formSchema)).toBe(true);
  });

  it('只接受受支持的静态表单协议和字段策略', () => {
    expect(isWorkflowTodoDetail(validDetail)).toBe(true);
    const { formSchemaHash: _, ...missingSchemaHash } = validDetail;
    expect(isWorkflowTodoDetail(missingSchemaHash)).toBe(false);
    expect(isWorkflowTodoDetail({
      ...validDetail,
      formSchemaHash: 'not-a-sha256'
    })).toBe(false);
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

  it.each([
    { ...validDetail.formSchema, sections: [] },
    {
      ...validDetail.formSchema,
      sections: [{ sectionKey: 'main', fields: [] }]
    },
    {
      ...validDetail.formSchema,
      sections: [
        validDetail.formSchema.sections[0],
        { sectionKey: 'request', fields: [{ ...validDetail.formSchema.sections[0].fields[0], fieldKey: 'other' }] }
      ]
    },
    {
      ...validDetail.formSchema,
      sections: [{ ...validDetail.formSchema.sections[0], sectionKey: 'bad key' }]
    },
    {
      ...validDetail.formSchema,
      sections: [{
        ...validDetail.formSchema.sections[0],
        fields: [{ ...validDetail.formSchema.sections[0].fields[0], fieldKey: '__proto__' }]
      }]
    },
    {
      ...validDetail.formSchema,
      sections: [{
        ...validDetail.formSchema.sections[0],
        fields: Array.from({ length: 65 }, (_, index) => ({
          ...validDetail.formSchema.sections[0].fields[0],
          fieldKey: `field${index}`
        }))
      }]
    },
    {
      ...validDetail.formSchema,
      sections: Array.from({ length: 33 }, (_, index) => ({
        sectionKey: `section${index}`,
        fields: [{ ...validDetail.formSchema.sections[0].fields[0], fieldKey: `field${index}` }]
      }))
    },
    {
      ...validDetail.formSchema,
      sections: Array.from({ length: 5 }, (_, sectionIndex) => ({
        sectionKey: `section${sectionIndex}`,
        fields: Array.from({ length: sectionIndex === 4 ? 1 : 64 }, (_, fieldIndex) => ({
          ...validDetail.formSchema.sections[0].fields[0],
          fieldKey: `field${sectionIndex}_${fieldIndex}`
        }))
      }))
    }
  ])('拒绝空结构、重复或危险稳定标识以及超限结构', schema => {
    expect(isWorkflowFormSchema(schema)).toBe(false);
  });

  it.each(['placeholder', 'script', 'css', 'html', 'remoteUrl', '__proto__'])(
    '拒绝字段类型未声明或危险的设计态约束：%s',
    constraintKey => {
      expect(isWorkflowFormSchema({
        ...validDetail.formSchema,
        sections: [{
          ...validDetail.formSchema.sections[0],
          fields: [{
            ...validDetail.formSchema.sections[0].fields[0],
            constraints: { [constraintKey]: 'unsafe design metadata' }
          }]
        }]
      })).toBe(false);
    }
  );
});
