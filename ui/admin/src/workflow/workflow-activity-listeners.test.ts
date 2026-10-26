import { describe, expect, it } from 'vitest';
import { readWorkflowActivityListeners } from './workflow-activity-listeners';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';

const listener = { listenerKey: 'done', eventKey: 'completed', actionKey: 'notify.initiator', actionVersion: 1 };
describe('受控活动监听器', () => {
  it('规范化为独立副本，设计资产往返保留固定版本', () => {
    const parsed = readWorkflowActivityListeners([listener]);
    expect(parsed).toEqual([listener]); expect(parsed[0]).not.toBe(listener);
    const draft = { schemaVersion: 1, workflowKindKey: 'approval', nodes: [
      { nodeKey: 'start', nodeTypeKey: 'start', nodeSchemaVersion: 1, config: { nextNodeKeys: ['review'] } },
      { nodeKey: 'review', nodeTypeKey: 'human.approval', nodeSchemaVersion: 1, config: { nextNodeKeys: ['end'], listeners: parsed } },
      { nodeKey: 'end', nodeTypeKey: 'end', nodeSchemaVersion: 1, config: {} }
    ] };
    const roundtrip = fromWorkflowVue3Tree(toWorkflowVue3Tree(draft));
    expect(roundtrip.nodes.find(node => node.nodeKey === 'review')?.config).toMatchObject({ listeners: [listener] });
  });
  it.each([null, {}, [{ ...listener, script: 'run()' }], [{ ...listener, actionKey: 'http' }],
    [{ ...listener, actionVersion: '1' }], [{ ...listener, eventKey: 'started' }], [{ ...listener, eventKey: ['completed'] }], [listener, listener],
    [{ ...listener, listenerKey: 'bad key' }], Array.from({ length: 9 }, (_, index) => ({ ...listener, listenerKey: `key${index}` }))])(
    '拒绝未知或歧义配置 %j', value => expect(() => readWorkflowActivityListeners(value)).toThrow('client.invalid_workflow_activity_listeners'));
});
