import { describe, expect, it } from 'vitest';
import {
  cloneDesignerJson,
  createVForm3Widget,
  moveVForm3Widget,
  removeVForm3Widget
} from './vform3-schema';

describe('VForm3 ESM JSON 内核', () => {
  it('深克隆合法设计态 JSON，避免调用方继续修改内部状态', () => {
    const source = {
      widgetList: [{ id: 'fn-amount', type: 'number', options: { name: 'amount' } }],
      formConfig: { labelWidth: 120 }
    };

    const cloned = cloneDesignerJson(source);
    source.widgetList[0]!.options.name = 'changed';

    expect(cloned.widgetList[0]?.options.name).toBe('amount');
  });

  it('拒绝数组以外的 widgetList 和带函数的非 JSON 配置', () => {
    expect(() => cloneDesignerJson({ widgetList: {}, formConfig: {} }))
      .toThrow('client.invalid_vform3_json');
    expect(() => cloneDesignerJson({
      widgetList: [],
      formConfig: { handler: () => undefined }
    })).toThrow('client.invalid_vform3_json');
  });

  it('创建、移动和删除字段时保持不可变更新', () => {
    const first = createVForm3Widget('input', () => 'first');
    const second = createVForm3Widget('number', () => 'second');
    const source = [first, second];

    const moved = moveVForm3Widget(source, 1, -1);
    const removed = removeVForm3Widget(moved, 0);

    expect(source.map(widget => widget.options.name)).toEqual(['input_first', 'number_second']);
    expect(moved.map(widget => widget.options.name)).toEqual(['number_second', 'input_first']);
    expect(removed.map(widget => widget.options.name)).toEqual(['input_first']);
  });

  it('无损保留 JSON 安全的 VForm3 顶层元数据', () => {
    const cloned = cloneDesignerJson({
      widgetList: [{
        id: 'fn-contract',
        type: 'input',
        icon: 'text-field',
        formItemFlag: true,
        options: { name: 'contract_name' }
      }],
      formConfig: {}
    });

    expect(cloned.widgetList[0]).toMatchObject({ icon: 'text-field', formItemFlag: true });
  });
});
