import { describe, expect, it } from 'vitest';
import { readWorkflowConnector } from './workflow-connector';
import { fromWorkflowVue3Tree, toWorkflowVue3Tree } from './workflow-vue3-adapter';
const config = { fieldPolicies: { title: 'readOnly', count: 'editable' }, connectorPolicy: { connectorKey: 'credit.check', connectorVersion: 1, inputFields: { subject: 'title' }, outputFields: { count: 'score' } } };
describe('受控连接器配置', () => {
  it('保留固定版本和闭合映射，无损往返嵌套树', () => {
    const tree = { id: 'start', type: 0, childNode: { id: 'call', type: 6, ...config, childNode: null } };
    const draft = fromWorkflowVue3Tree(tree); expect(draft.nodes.find(node => node.nodeKey === 'call')).toMatchObject({ nodeTypeKey: 'integration.trigger', config });
    expect(toWorkflowVue3Tree(draft)).toMatchObject(tree); expect(readWorkflowConnector(config)).toEqual(config);
  });
  it.each([
    { ...config, connectorPolicy: { ...config.connectorPolicy, url: 'https://example.org' } },
    { ...config, connectorPolicy: { ...config.connectorPolicy, connectorVersion: '1' } },
    { ...config, connectorPolicy: { ...config.connectorPolicy, outputFields: { title: 'score' } } },
    { ...config, connectorPolicy: { ...config.connectorPolicy, inputFields: { subject: 'private' } } },
    { ...config, connectorPolicy: { ...config.connectorPolicy, outputFields: { count: 'result[0].score' } } },
    { ...config, connectorPolicy: { ...config.connectorPolicy, headers: { Authorization: 'secret' } } },
    { ...config, boundConnectorHash: 'client-assertion' }
  ])('拒绝未知连接配置、越权映射和非法摘要', value => expect(() => readWorkflowConnector(value)).toThrow());
  it('回显保留服务器绑定摘要，但普通地址配置仍失败关闭', () => {
    const value = { ...config, boundConnectorHash: 'a'.repeat(64) }; expect(readWorkflowConnector(value)).toEqual(value);
    expect(() => fromWorkflowVue3Tree({ id: 'start', type: 0, childNode: { id: 'call', type: 6, ...config, url: 'https://example.org', childNode: null } })).toThrow();
  });
});

it('异步连接器与消费者依赖在树和协议之间无损往返', () => {
  const tree = { id: 'start', type: 0, childNode: { id: 'call', type: 11, ...config,
    childNode: { id: 'consumer', type: 22, awaitActivityNodeKeys: ['call'], childNode: null } } };
  const draft = fromWorkflowVue3Tree(tree);
  expect(draft.nodes.find(node => node.nodeKey === 'call')?.nodeTypeKey).toBe('integration.async_trigger');
  expect(draft.nodes.find(node => node.nodeKey === 'consumer')).toMatchObject({ awaitActivityNodeKeys: ['call'], config: { nextNodeKeys: ['end'] } });
  expect(draft.nodes.find(node => node.nodeKey === 'consumer')?.config).not.toHaveProperty('awaitActivityNodeKeys');
  expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
  for (const keys of [['call', 'call'], ['bad key'], Array.from({ length: 17 }, (_, index) => `call${index}`), 'call']) {
    expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, awaitActivityNodeKeys: keys } })).toThrow();
  }
});

it('结束边界的导入依赖无损保存，不能把边界元数据藏在普通节点', () => {
  const tree = { id: 'start', type: 0, boundaryAwaitActivityNodeKeys: { end: ['call'] },
    childNode: { id: 'call', type: 11, ...config, childNode: null } };
  const draft = fromWorkflowVue3Tree(tree);
  expect(draft.nodes.find(node => node.nodeKey === 'end')).toMatchObject({ awaitActivityNodeKeys: ['call'] });
  expect(toWorkflowVue3Tree(draft)).toMatchObject(tree);
  expect(() => fromWorkflowVue3Tree({ ...tree, childNode: { ...tree.childNode, boundaryAwaitActivityNodeKeys: { end: ['call'] } } })).toThrow();
  expect(() => fromWorkflowVue3Tree({ ...tree, boundaryAwaitActivityNodeKeys: { call: ['call'] } })).toThrow();
});
