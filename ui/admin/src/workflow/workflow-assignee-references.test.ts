import { describe, expect, it } from 'vitest';
import type { WorkflowFormField } from '@fullnet/client-contracts';
import { workflowAssigneeReferenceFields, workflowPreviousActorNodes } from './workflow-assignee-references';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

describe('动态办理人引用', () => {
  it('只提供使用稳定标识选项的单选和多选字段', () => {
    const fields: WorkflowFormField[] = [
      { fieldKey: 'users', fieldTypeKey: 'checkbox', required: false, constraints: { options: ['01912345-6789-7abc-8def-0123456789ab'] } },
      { fieldKey: 'note', fieldTypeKey: 'text', required: false, constraints: {} },
      { fieldKey: 'label', fieldTypeKey: 'select', required: false, constraints: { options: ['display-name'] } }
    ];
    expect(workflowAssigneeReferenceFields(fields).map(field => field.fieldKey)).toEqual(['users']);
    expect(workflowAssigneeReferenceFields([{ ...fields[0]!, constraints: { options: [
      '01912345-6789-7abc-8def-0123456789ab', '01912345-6789-7ABC-8DEF-0123456789AB'] } }])).toEqual([]);
  });
  it('发起人自选范围无损往返，单选人数和跨范围属性失败关闭', () => {
    const user = '01912345-6789-7abc-8def-0123456789ab';
    for (const range of [{ kindKey: 'all' }, { kindKey: 'specified_users', userIds: [user] }, { kindKey: 'role_members', roleIds: [user] }]) {
      const selectionPolicy = { selectionKey: 'review', modeKey: 'single', maximumUsers: 1, range };
      const tree = { id: 'start', type: 0, childNode: { id: 'task', type: 18, taskPolicy: { completionModeKey: 'all' },
        assigneePolicy: { sources: [{ resolverKindKey: 'starter_selected_users', selectionPolicy }] }, childNode: null } };
      expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
      for (const bad of [{ ...selectionPolicy, maximumUsers: 2 }, { ...selectionPolicy, range: { ...range, tenantId: user } }]) {
        expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode,
          assigneePolicy: { sources: [{ resolverKindKey: 'starter_selected_users', selectionPolicy: bad }] } } })).toThrow();
      }
    }
  });
  it('前序来源只显示必经已在前的活动，不能选择目标自身或后继', () => {
    const draft = fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'first', type: 18, taskPolicy: { completionModeKey: 'all' },
      childNode: { id: 'second', type: 18, taskPolicy: { completionModeKey: 'all' },
      childNode: { id: 'third', type: 18, taskPolicy: { completionModeKey: 'all' }, childNode: null } } } });
    expect(workflowPreviousActorNodes(draft, 'second').map(node => node.key)).toEqual(['first']);
  });
  it('动态来源正反转换保留所有参数，拒绝客户端夹带成员或身份', () => {
    for (const source of [{ resolverKindKey: 'form_users', fieldKey: 'users' }, { resolverKindKey: 'form_units', fieldKey: 'units' },
      { resolverKindKey: 'previous_node_actors', nodeKey: 'first' }]) {
      const tree = { id: 'start', type: 0, childNode: { id: 'first', type: 18, taskPolicy: { completionModeKey: 'all' }, childNode: { id: 'second', type: 18, taskPolicy: { completionModeKey: 'all' },
        assigneePolicy: { sources: [source] }, childNode: null } } };
      expect(toWorkflowVue3Tree(fromWorkflowVue3Tree(tree))).toMatchObject(tree);
      expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, assigneePolicy: { sources: [{ ...source, actorUserId: 'external' }] } } })).toThrow();
    }
  });
});
