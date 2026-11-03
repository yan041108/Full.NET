import { describe, expect, it } from 'vitest';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
import { readWorkflowCollaboration } from './workflow-collaboration-node';
const policy = { schemaVersion: 1, grantId: '11111111-1111-4111-8111-111111111111', grantRevision: 2, policyHash: 'a'.repeat(64) };
describe('协作类型20的固定协议', () => {
  it('服务端固定政策与设计器类型20无损往返', () => {
    const tree = { id: 'start', type: 0, workflowKindKey: 'automation', childNode: { id: 'collaboration', type: 20, collaborationPolicy: policy, childNode: null } };
    const draft = fromWorkflowVue3Tree(tree); expect(draft.nodes.find(node => node.nodeKey === 'collaboration')).toMatchObject({ nodeTypeKey: 'process.collaboration', config: { collaborationPolicy: policy } });
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
  });
  it.each([{ ...policy, targetTenantId: policy.grantId }, { ...policy, allowedUserIds: [] }, { ...policy, grantRevision: 3 },
    { ...policy, policyHash: 'x'.repeat(64) }, { ...policy, grantId: '00000000-0000-0000-0000-000000000000' }])('拒绝调用方扩张政策或非法身份', value => {
    expect(() => readWorkflowCollaboration({ collaborationPolicy: value })).toThrow('client.invalid_workflow_collaboration');
  });
});
