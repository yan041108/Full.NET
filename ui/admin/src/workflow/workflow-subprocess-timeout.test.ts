import { describe, expect, it } from 'vitest';
import { readWorkflowSubprocessTimeout } from './workflow-subprocess';

describe('子流程超时闭合配置', () => {
  it('保留显式暂停方式及静态后继', () => {
    const value = { dueAfterMinutes: 60, pauseModeKey: 'freeze', actionKey: 'route', nextNodeKey: 'fallback' };
    expect(readWorkflowSubprocessTimeout(value)).toEqual(value);
    expect(readWorkflowSubprocessTimeout({ dueAfterMinutes: 1, pauseModeKey: 'elapsed', actionKey: 'continue' }).actionKey).toBe('continue');
  });
  it('拒绝缺失目标、隐式暂停语义、越界与额外属性', () => {
    for (const value of [
      { dueAfterMinutes: 0, pauseModeKey: 'freeze', actionKey: 'continue' },
      { dueAfterMinutes: 1, actionKey: 'continue' },
      { dueAfterMinutes: 1, pauseModeKey: 'freeze', actionKey: 'route' },
      { dueAfterMinutes: 1, pauseModeKey: 'freeze', actionKey: 'continue', nextNodeKey: 'fallback' },
      { dueAfterMinutes: 1, pauseModeKey: 'freeze', actionKey: 'continue', tenantId: 'host' }
    ]) expect(() => readWorkflowSubprocessTimeout(value)).toThrow();
  });
});
