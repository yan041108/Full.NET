import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import VForm3DesignerHost from './VForm3DesignerHost.vue';
import { VFormFrameClient } from './frame-client';
import { VFORM_RUNTIME_HASH } from './runtime-release.generated';
describe('VForm3 隔离宿主', () => {
  it.each([VFORM_RUNTIME_HASH, 'different-build'])('已就绪帧再次握手（%s）使旧会话失效，拒绝未完成请求与后续数据', async runtimeHash => {
    vi.useFakeTimers();
    const messages: { id: string }[] = [];
    const frame = { postMessage: (message: { id: string }) => messages.push(message) } as unknown as Window;
    const ready = vi.fn(); const changed = vi.fn(); const errors = vi.fn();
    const client = new VFormFrameClient(() => frame, ready, window, changed, errors);
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    try {
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      const pending = client.request('getFormJson').then(() => 'unexpected-success', (error: Error) => error.message);
      dispatch({ ready: true, runtimeHash });
      dispatch({ id: messages[0].id, result: { widgetList: [], formConfig: {} } });
      dispatch({ changed: { title: '重载后的空画布' } });
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      expect(await pending).toBe(runtimeHash === VFORM_RUNTIME_HASH ? 'client.vform3_operation_failed' : 'client.vform3_runtime_mismatch');
      expect(ready).toHaveBeenCalledTimes(1);
      expect(changed).not.toHaveBeenCalled();
      expect(errors).toHaveBeenCalledTimes(1);
      await expect(client.request('getFormJson')).rejects.toThrow('client.vform3_not_ready');
      expect(messages).toHaveLength(1);
      expect(vi.getTimerCount()).toBe(0);
    } finally { client.dispose(); vi.useRealTimers(); }
  });

  it.each(['load', 'error'] as const)('原生 iframe %s 使当前请求立即失败，重载后的空数据不能交给保存方', async event => {
    const wrapper = mount(VForm3DesignerHost, { attachTo: document.body });
    const iframe = wrapper.get('iframe');
    const frame = (iframe.element as HTMLIFrameElement).contentWindow!;
    const send = vi.spyOn(frame, 'postMessage').mockImplementation(() => {});
    const channel = new URLSearchParams(new URL((iframe.element as HTMLIFrameElement).src).hash.slice(1)).get('channel');
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel, ...data } }));
    try {
      await iframe.trigger('load');
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      const pending = wrapper.vm.getFormJson().then(() => 'unexpected-success', (error: Error) => error.message);
      await iframe.trigger(event);
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      const message = send.mock.calls[0][0] as { id: string };
      dispatch({ id: message.id, result: { widgetList: [], formConfig: {} } });
      const expected = event === 'load' ? 'client.vform3_operation_failed' : 'client.vform3_install_failed';
      expect(await pending).toBe(expected);
      expect(wrapper.emitted('error')).toEqual([[expected]]);
      expect(wrapper.emitted('ready')).toHaveLength(1);
      expect(wrapper.attributes('aria-busy')).toBe('true');
      await expect(wrapper.vm.getFormJson()).rejects.toThrow('client.vform3_not_ready');
    } finally { wrapper.unmount(); send.mockRestore(); }
  });

  it('完整设计器仅运行在无同源权限的 iframe 中', () => {
    const wrapper = mount(VForm3DesignerHost);
    expect(wrapper.get('iframe').attributes('sandbox')).toBe('allow-scripts allow-downloads');
    expect(wrapper.get('iframe').attributes('src')).toContain('/vform3/index.html#parent=');
    wrapper.unmount();
  });
  it('拒绝错误窗口、来源和通道，匹配请求后才返回完整设计 JSON', async () => {
    const messages: unknown[] = [];
    const frame = { postMessage: (message: unknown) => messages.push(message) } as unknown as Window;
    let ready = false; const client = new VFormFrameClient(() => frame, () => { ready = true; });
    const dispatch = (data: object, origin = 'null', source = frame) => window.dispatchEvent(new MessageEvent('message', { data: { protocol: 'fullnet.vform3.v1', channel: client.channel, runtimeHash: VFORM_RUNTIME_HASH, ...data }, origin, source }));
    dispatch({ ready: true }, 'https://evil.example'); expect(ready).toBe(false);
    dispatch({ ready: true, channel: 'wrong' }); expect(ready).toBe(false);
    dispatch({ ready: true }, 'null', window); expect(ready).toBe(false);
    dispatch({ ready: true }); expect(ready).toBe(true);
    const result = client.request('getFormJson'); const request = messages[0] as { id: string };
    const json = { widgetList: [], formConfig: { onMounted: 'return 1;', custom: { a: true } } };
    dispatch({ id: request.id, result: json }); expect(await result).toEqual(json);
    client.dispose();
  });
  it('卸载拒绝未完成调用，避免切换表单后迟到回执污染新宿主', async () => {
    const frame = { postMessage() {} } as unknown as Window;
    const client = new VFormFrameClient(() => frame, () => {});
    window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, runtimeHash: VFORM_RUNTIME_HASH, ready: true } }));
    const result = client.request('getFormJson'); client.dispose();
    await expect(result).rejects.toThrow('client.vform3_disposed');
  });
  it('旧运行产物不能通过 Ready 声明替换当前受审构建', async () => {
    const frame = { postMessage() {} } as unknown as Window; let ready = false; let error = '';
    const client = new VFormFrameClient(() => frame, () => { ready = true; }, window, undefined, code => { error = code; });
    window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, runtimeHash: 'old-build', ready: true } }));
    expect(ready).toBe(false); expect(error).toBe('client.vform3_runtime_mismatch');
    await expect(client.request('getFormData')).rejects.toThrow('client.vform3_not_ready'); client.dispose();
  });
  it('Ready 摘要核验之前不向业务回调转交变更', () => {
    const frame = { postMessage() {} } as unknown as Window; const changed = vi.fn();
    const client = new VFormFrameClient(() => frame, () => {}, window, changed);
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    try {
      dispatch({ changed: { title: '未核验' } });
      dispatch({ ready: true, runtimeHash: 'old-build' });
      dispatch({ changed: { title: '错误产物' } });
      expect(changed).not.toHaveBeenCalled();
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      dispatch({ changed: { title: '核验通过' } });
      expect(changed).toHaveBeenCalledExactlyOnceWith({ title: '核验通过' });
    } finally { client.dispose(); }
  });
  it.each(['循环引用', 'BigInt', '超限字段', '超限信封', '空变更', '数组变更', '文本变更', '非 JSON 对象'])('当前帧的%s变更必须使会话失败，不能继续使用旧补丁', async kind => {
    vi.useFakeTimers();
    const frame = { postMessage() {} } as unknown as Window; const changed = vi.fn(); const failure = vi.fn(); const errors: string[] = [];
    const client = new VFormFrameClient(() => frame, () => {}, window, changed, failure);
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    const onError = (event: ErrorEvent) => { errors.push(event.message); event.preventDefault(); };
    window.addEventListener('error', onError);
    try {
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      dispatch({ changed: { title: '先前有效输入' } });
      const pending = client.request('getFormJson').then(() => 'unexpected-success', (error: Error) => error.message);
      const object: Record<string, unknown> = { title: kind === 'BigInt' ? 1n : 'x'.repeat(kind === '超限字段' ? 2_000_001 : kind === '超限信封' ? 2_100_001 : 1) };
      if (kind === '循环引用') object.self = object;
      const invalid = kind === '空变更' ? null : kind === '数组变更' ? [] : kind === '文本变更' ? 'invalid'
        : kind === '非 JSON 对象' ? new Date('2026-10-24T00:00:00Z') : object;
      dispatch({ changed: invalid });
      const code = kind === '超限字段' || kind === '超限信封' ? 'client.vform3_payload_too_large' : 'client.vform3_operation_failed';
      expect(failure).toHaveBeenCalledExactlyOnceWith(code);
      expect(await pending).toBe(code);
      expect(errors).toEqual([]);
      dispatch({ changed: { title: '合法变更' } });
      expect(changed).toHaveBeenCalledExactlyOnceWith({ title: '先前有效输入' });
      await expect(client.request('getFormData')).rejects.toThrow('client.vform3_not_ready');
      expect(vi.getTimerCount()).toBe(0);
    } finally { window.removeEventListener('error', onError); client.dispose(); vi.useRealTimers(); }
  });
  it('错误来源的非法变更不能中断已经授权的会话', () => {
    const frame = { postMessage() {} } as unknown as Window; const changed = vi.fn(); const failure = vi.fn();
    const client = new VFormFrameClient(() => frame, () => {}, window, changed, failure);
    const dispatch = (data: object, origin = 'null', source = frame) => window.dispatchEvent(new MessageEvent('message', { source, origin, data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    try {
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      dispatch({ changed: null }, 'https://evil.example');
      dispatch({ changed: null }, 'null', window);
      dispatch({ changed: null, channel: 'wrong' });
      dispatch({ changed: { title: '正常输入' } });
      expect(failure).not.toHaveBeenCalled();
      expect(changed).toHaveBeenCalledExactlyOnceWith({ title: '正常输入' });
    } finally { client.dispose(); }
  });
  it('超限调用回执立即拒绝，不把完整负载交给读取方或等待超时', async () => {
    vi.useFakeTimers();
    const messages: { id: string }[] = [];
    const frame = { postMessage: (message: { id: string }) => messages.push(message) } as unknown as Window;
    const client = new VFormFrameClient(() => frame, () => {});
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    try {
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      const result = client.request('getFormJson').then(() => 'resolved', (error: Error) => error.message);
      dispatch({ id: messages[0].id, result: { widgetList: [], formConfig: { title: 'x'.repeat(2_100_001) } } });
      await vi.runAllTimersAsync(); expect(await result).toBe('client.vform3_payload_too_large');
      const next = client.request('getFormJson'); const json = { widgetList: [], formConfig: {} };
      dispatch({ id: messages[1].id, result: json }); expect(await next).toEqual(json);
      expect(vi.getTimerCount()).toBe(0);
    } finally { client.dispose(); vi.useRealTimers(); }
  });
  it.each(['循环引用', 'BigInt'])('不能将含%s的非 JSON 回执当作成功结果', async kind => {
    vi.useFakeTimers(); const messages: { id: string }[] = [];
    const frame = { postMessage: (message: { id: string }) => messages.push(message) } as unknown as Window;
    const client = new VFormFrameClient(() => frame, () => {});
    const dispatch = (data: object) => window.dispatchEvent(new MessageEvent('message', { source: frame, origin: 'null', data: { protocol: 'fullnet.vform3.v1', channel: client.channel, ...data } }));
    try {
      dispatch({ ready: true, runtimeHash: VFORM_RUNTIME_HASH });
      const result = client.request('getFormJson').then(() => 'resolved', (error: Error) => error.message);
      const invalid: Record<string, unknown> = { value: kind === 'BigInt' ? 1n : null };
      if (kind === '循环引用') invalid.self = invalid;
      dispatch({ id: messages[0].id, result: invalid });
      await vi.runAllTimersAsync(); expect(await result).toBe('client.vform3_operation_failed');
      expect(vi.getTimerCount()).toBe(0);
    } finally { client.dispose(); vi.useRealTimers(); }
  });
});
