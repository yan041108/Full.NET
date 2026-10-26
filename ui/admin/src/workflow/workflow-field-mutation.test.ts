import { describe, expect, it } from 'vitest';
import { readWorkflowFieldMutation } from './workflow-field-mutation';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

describe('Workflow field mutations', () => {
  const modify = { fieldPolicies: { first: 'editable', second: 'readOnly' }, mutationPolicy: { assignments: [{ fieldKey: 'first', sourceFieldKey: 'second' }] } };
  it('projects a fixed source copy without mutating the input', () => {
    const before = JSON.stringify(modify); expect(readWorkflowFieldMutation('data.modify', modify)).toEqual(modify); expect(JSON.stringify(modify)).toBe(before);
  });
  it.each([
    { fieldPolicies: {}, mutationPolicy: modify.mutationPolicy },
    { fieldPolicies: { first: 'readOnly', second: 'readOnly' }, mutationPolicy: modify.mutationPolicy },
    { fieldPolicies: { first: 'editable', second: 'hidden' }, mutationPolicy: modify.mutationPolicy },
    { ...modify, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'x', sourceFieldKey: 'second' }] } },
    { ...modify, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'x', script: 'run' }] } },
    { ...modify, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'x' }, { fieldKey: 'first', value: 'y' }] } },
    { ...modify, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'x'.repeat(16385) }] } },
    { ...modify, mutationPolicy: { ...modify.mutationPolicy, condition: { fieldKey: 'unknown', operator: 'isEmpty' } } }
  ])('rejects unauthorized, ambiguous, repeated or oversized values', value => expect(() => readWorkflowFieldMutation('data.modify', value)).toThrow());
  it('keeps conditional clear distinct from business record deletion', () => {
    const value = { fieldPolicies: { first: 'editable', second: 'readOnly' }, mutationPolicy: { fieldKeys: ['first'], condition: { fieldKey: 'second', operator: 'isNotEmpty' } } };
    expect(readWorkflowFieldMutation('data.clear', value)).toEqual(value);
    expect(() => readWorkflowFieldMutation('data.clear', { ...value, fieldPolicies: { ...value.fieldPolicies, first: 'required' } })).toThrow();
  });
  it('round trips modify and clear through the actual tree adapter', () => {
    const tree = { id: 'start', type: 0, nodeName: 'Start', workflowKindKey: 'automation', childNode: {
      id: 'modify', type: 15, nodeName: 'Modify', ...modify, childNode: {
        id: 'clear', type: 16, nodeName: 'Clear', fieldPolicies: { second: 'editable' }, mutationPolicy: { fieldKeys: ['second'] }, childNode: null } } };
    const draft = fromWorkflowVue3Tree(tree);
    expect(draft.nodes.find(node => node.nodeKey === 'modify')?.nodeTypeKey).toBe('data.modify');
    expect(draft.nodes.find(node => node.nodeKey === 'clear')?.nodeTypeKey).toBe('data.clear');
    expect(fromWorkflowVue3Tree(toWorkflowVue3Tree(draft))).toEqual(draft);
  });
  it('treats URL text constants as field data while still rejecting connector options', () => {
    const node = { id: 'modify', type: 15, fieldPolicies: { first: 'editable' }, mutationPolicy: { assignments: [{ fieldKey: 'first', value: 'https://example.test/profile' }] }, childNode: null };
    expect(fromWorkflowVue3Tree({ id: 'start', type: 0, workflowKindKey: 'automation', childNode: node }).nodes.find(item => item.nodeKey === 'modify')?.config).toMatchObject({ mutationPolicy: node.mutationPolicy });
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, workflowKindKey: 'automation', childNode: { ...node, url: 'https://example.test/' } })).toThrow();
  });
});
