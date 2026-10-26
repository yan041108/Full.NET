import { describe, expect, it } from 'vitest';
import { isWorkflowFormSchema, isWorkflowTodoDetail, readWorkflowSignatureUpload } from '../src/workflow-todos';
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
  allowedActions: ['approve', 'reject', 'return', 'countersign', 'transfer', 'remind'], signRequired: false, commentRequired: false,
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
  it('领取办理必须携带独立池修订且不能伪造审批、任务成员或选票', () => {
    const claim = { ...validDetail, activityKindKey: 'human.claim', approvalModeKey: 'none', requiredApprovalCount: 0,
      approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['complete', 'release', 'transfer'],
      claimProgress: { poolStatusKey: 'claimed', poolRevision: 2, claimCycle: 1, allowRelease: true } };
    expect(isWorkflowTodoDetail(claim)).toBe(true);
    expect(isWorkflowTodoDetail({ ...claim, claimProgress: null })).toBe(false);
    expect(isWorkflowTodoDetail({ ...claim, allowedActions: ['approve'] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...claim, requiredApprovalCount: 1 })).toBe(false);
    expect(isWorkflowTodoDetail({ ...validDetail, claimProgress: claim.claimProgress })).toBe(false);
  });
  it('投票有独立表单修订且活动中不泄露选择汇总', () => {
    const vote = { ...validDetail, activityKindKey: 'human.vote', approvalModeKey: 'none', requiredApprovalCount: 0,
      approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['vote', 'transfer'],
      fieldPolicies: { summary: 'readOnly' }, voteForm: { formVersionId: validDetail.formVersionId,
        formSchemaHash: validDetail.formSchemaHash, formSchema: validDetail.formSchema, values: {}, fieldPolicies: { summary: 'required' },
        revision: 1, choiceKey: null, readOnly: false, allowAbstain: true, webRenderJson: null },
      voteProgress: { memberCount: 3, requiredParticipationCount: 2, submittedCount: 1, pendingCount: 2, cancelledCount: 0,
        statusKey: 'active', closesAtUtc: null, yesCount: null, noCount: null, abstainCount: null, unsubmittedCount: null } };
    expect(isWorkflowTodoDetail(vote)).toBe(true);
    expect(isWorkflowTodoDetail({ ...vote, voteForm: null })).toBe(false);
    expect(isWorkflowTodoDetail({ ...vote, allowedActions: ['approve'] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...vote, approvedCount: 1 })).toBe(false);
    expect(isWorkflowTodoDetail({ ...vote, voteForm: { ...vote.voteForm, revision: 0 } })).toBe(false);
    expect(isWorkflowTodoDetail({ ...vote, voteProgress: { ...vote.voteProgress, yesCount: 1 } })).toBe(false);
    expect(isWorkflowTodoDetail({ ...vote, voteProgress: { ...vote.voteProgress, memberCount: 4 } })).toBe(false);
    const completed = { ...vote, voteForm: { ...vote.voteForm, values: { summary: '本人意见' }, fieldPolicies: { summary: 'readOnly' },
      readOnly: true, revision: 2, choiceKey: 'yes' }, voteProgress: { ...vote.voteProgress, statusKey: 'approved',
      pendingCount: 0, cancelledCount: 2, yesCount: 1, noCount: 0, abstainCount: 0, unsubmittedCount: 2 } };
    expect(isWorkflowTodoDetail(completed)).toBe(true);
    expect(isWorkflowTodoDetail({ ...completed, voteProgress: { ...completed.voteProgress, unsubmittedCount: 0 } })).toBe(false);
  });
  it('办理必须带独立完成进度，不能混用审批票或审批动作', () => {
    const task = { ...validDetail, activityKindKey: 'human.task', approvalModeKey: 'none', requiredApprovalCount: 0,
      approvedCount: 0, rejectedCount: 0, pendingCount: 0, allowedActions: ['complete', 'transfer'],
      taskProgress: { completionModeKey: 'all', memberCount: 3, requiredCompletionCount: 3, completedCount: 1, pendingCount: 2, cancelledCount: 0 } };
    expect(isWorkflowTodoDetail(task)).toBe(true);
    expect(isWorkflowTodoDetail({ ...task, taskProgress: undefined })).toBe(false);
    expect(isWorkflowTodoDetail({ ...task, approvedCount: 1 })).toBe(false);
    expect(isWorkflowTodoDetail({ ...task, allowedActions: ['approve'] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...task, taskProgress: { ...task.taskProgress, completionModeKey: 'any', requiredCompletionCount: 1 } })).toBe(true);
    expect(isWorkflowTodoDetail({ ...task, taskProgress: { ...task.taskProgress, memberCount: 4 } })).toBe(false);
  });

  it('组进度只接受真实组票，终态取消组不伪装成待定', () => {
    const group = (number: number) => ({ id: `01912345-6789-7abc-8def-0123456789a${number}`, groupKey: `group_${number}`, groupName: null,
      sequenceNo: number, approvalModeKey: 'all', requiredApprovalCount: 2, memberCount: 2, approvedCount: 0,
      rejectedCount: 0, pendingCount: 2, cancelledCount: 0, statusKey: 'active', isCurrentGroup: number === 1 });
    const detail = { ...validDetail, approvalModeKey: 'group', requiredApprovalCount: 2, pendingCount: 2, approvalGroups: [group(1), group(2)] };
    expect(isWorkflowTodoDetail(detail)).toBe(true);
    const withoutCurrent = { ...detail, approvalGroups: detail.approvalGroups.map(group => ({ ...group, isCurrentGroup: false })) };
    expect(isWorkflowTodoDetail(withoutCurrent)).toBe(false);
    expect(isWorkflowTodoDetail({ ...withoutCurrent, statusKey: 'completed' })).toBe(true);
    expect(isWorkflowTodoDetail({ ...withoutCurrent, statusKey: 'cancelled' })).toBe(true);
    expect(isWorkflowTodoDetail({ ...detail, statusKey: 'completed', approvalGroups: detail.approvalGroups.map(group => ({ ...group, isCurrentGroup: true })) })).toBe(false);
    expect(isWorkflowTodoDetail({ ...detail, approvedCount: 1 })).toBe(false);
    const system = { ...group(2), approvalModeKey: 'system', requiredApprovalCount: 0, memberCount: 0,
      pendingCount: 0, statusKey: 'approved' };
    const mixed = { ...detail, approvedCount: 1, pendingCount: 1, approvalGroups: [group(1), system] };
    expect(isWorkflowTodoDetail(mixed)).toBe(true);
    expect(isWorkflowTodoDetail({ ...mixed, approvalGroups: [group(1), { ...system, approvedCount: 1 }] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...mixed, approvalGroups: [group(1), { ...system, isCurrentGroup: true }] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...mixed, approvalGroups: [group(1), { ...system, approvalModeKey: 'all' }] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...detail, approvalGroups: [group(1), { ...group(2), isCurrentGroup: true }] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...detail, pendingCount: 0, rejectedCount: 1, approvalGroups: [
      { ...group(1), statusKey: 'rejected', rejectedCount: 1, pendingCount: 0, cancelledCount: 1 },
      { ...group(2), statusKey: 'cancelled', pendingCount: 0, cancelledCount: 2 }] })).toBe(true);
  });

  it('接受已冻结门槛的比例和依次审批并继续拒绝未知模式', () => {
    expect(isWorkflowTodoDetail({ ...validDetail, approvalModeKey: 'percent',
      requiredApprovalCount: 2, approvedCount: 1, pendingCount: 2 })).toBe(true);
    expect(isWorkflowTodoDetail({ ...validDetail, approvalModeKey: 'percentage' })).toBe(false);
    expect(isWorkflowTodoDetail({ ...validDetail, approvalModeKey: 'sequential',
      requiredApprovalCount: 3, approvedCount: 1, pendingCount: 2 })).toBe(true);
  });

  it.each([
    { allowedActions: undefined }, { allowedActions: ['approve', 'admin'] }, { allowedActions: ['approve', 'approve'] },
    { signRequired: 'true' }, { commentRequired: undefined }
  ])('动作证据要求必须显式且闭合 %j', policy => {
    expect(isWorkflowTodoDetail({ ...validDetail, ...policy })).toBe(false);
  });

  it('采集响应只接受有界文件身份和摘要，不接受 URL', () => {
    expect(readWorkflowSignatureUpload({ fileId: validDetail.id, sizeBytes: 68, contentHash: 'a'.repeat(64) }).fileId).toBe(validDetail.id);
    for (const value of [{ fileId: 'https://files.example.test/signature.png', sizeBytes: 68, contentHash: null },
      { fileId: validDetail.id, sizeBytes: 1_048_577, contentHash: null }, { fileId: validDetail.id, sizeBytes: 68, contentHash: 'invalid' }]) {
      expect(() => readWorkflowSignatureUpload(value)).toThrow('workflow.todo.invalid-signature-upload');
    }
  });
  it.each([[], 'invalid', 42, true])('拒绝非对象的 VForm Web 投影 %j', webRenderJson => {
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson })).toBe(false);
  });

  it('VForm 对象投影、空投影和程序化表单均保留', () => {
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson: { widgetList: [], formConfig: { jsonVersion: 3 } } })).toBe(true);
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson: null })).toBe(true);
    expect(isWorkflowTodoDetail(validDetail)).toBe(true);
  });
  it('接受跨端共享的 v1 Golden Fixture', () => {
    expect(isWorkflowFormSchema(goldenFixture.formSchema)).toBe(true);
  });

  it('只接受受支持的静态表单协议和字段策略', () => {
    expect(isWorkflowTodoDetail(validDetail)).toBe(true);
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson: { widgetList: [] } })).toBe(true);
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson: [] })).toBe(false);
    expect(isWorkflowTodoDetail({ ...validDetail, webRenderJson: 'invalid' })).toBe(false);
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
