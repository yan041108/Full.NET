import { describe, expect, it, vi } from 'vitest';
import { deferLayuiRuntime } from '../js/core/layui-runtime.js';

describe('Layui 运行库延迟加载', () => {
  it('等待会话恢复和下一次绘制调度后再加载增强能力', async () => {
    let resolveReady;
    const ready = new Promise(resolve => {
      resolveReady = resolve;
    });
    const scheduled = [];
    const importRuntime = vi.fn().mockResolvedValue(undefined);
    const enhance = vi.fn();
    const runtime = deferLayuiRuntime({
      ready,
      schedule: callback => scheduled.push(callback),
      importRuntime,
      enhance
    });

    expect(importRuntime).not.toHaveBeenCalled();
    resolveReady();
    await Promise.resolve();
    await Promise.resolve();
    expect(scheduled).toHaveLength(1);
    expect(importRuntime).not.toHaveBeenCalled();

    scheduled[0]();
    await runtime.whenSettled;

    expect(importRuntime).toHaveBeenCalledOnce();
    expect(enhance).toHaveBeenCalledOnce();
  });
});
