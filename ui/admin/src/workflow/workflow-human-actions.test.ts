import { describe, expect, it } from 'vitest';
import { readWorkflowOperationPermissions } from './workflow-human-actions';

describe('published human action mapping', () => {
  it.each(['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'])('代理是独立发布动作且不扩大其他动作 %s', kind => {
    expect(readWorkflowOperationPermissions({ delegate: true, transfer: false }, kind))
      .toEqual({ delegate: true, transfer: false });
    expect(readWorkflowOperationPermissions([{ key: 'delegate', enable: true }], kind))
      .toEqual({ delegate: true });
    expect(() => readWorkflowOperationPermissions({ delegate_recall: true }, kind))
      .toThrow('client.invalid_workflow_approval_policy');
  });
  it('只按受控机器码映射旧按钮，显示名称不授予权限', () => {
    expect(readWorkflowOperationPermissions([{ key: 'pass', name: '管理员', enable: true },
      { key: 'back', enable: false }, { key: 'addSign', enable: true }, { key: 'urge', enable: false }]))
      .toEqual({ approve: true, return: false, countersign: true, remind: false });
  });
  it.each([[{ key: 'pass', enable: true }, { key: 'pass', enable: false }], [{ key: 'admin', enable: true }],
    [{ key: 'pass', enable: 'true' }], { approve: 1 }, { admin: true }])('未知、重复或伪造动作失败关闭 %j', value => {
    expect(() => readWorkflowOperationPermissions(value)).toThrow('client.invalid_workflow_approval_policy');
  });
});
