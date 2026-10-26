import { describe, expect, it } from 'vitest';
import { convertVFormValues, readVFormBindings } from './vform3-values';
describe('VForm 固定绑定值协议', () => {
  it('分钟精度业务时间显式补零供原版控件读取，秒精度输入原样提交', () => {
    const time = [{ fieldKey: 'cutoff', codec: 'iso-time' }];
    expect(convertVFormValues(time, { cutoff: '08:30' }, false)).toEqual({ cutoff: '08:30:00' });
    expect(convertVFormValues(time, { cutoff: '08:30:37' }, false)).toEqual({ cutoff: '08:30:37' });
    expect(convertVFormValues(time, { cutoff: '12:45:37' }, true)).toEqual({ cutoff: '12:45:37' });
    expect(convertVFormValues(time, { cutoff: '' }, true)).toEqual({ cutoff: null });
    expect(() => convertVFormValues(time, { cutoff: '24:00' }, false)).toThrow();
    expect(() => convertVFormValues(time, { cutoff: '12:45:60' }, true)).toThrow();
  });
  const bindings = [{ fieldKey: 'title', codec: 'string' }, { fieldKey: 'count', codec: 'safe-integer' }, { fieldKey: 'date', codec: 'iso-date' }, { fieldKey: 'choices', codec: 'string-array' }, { fieldKey: 'enabled', codec: 'boolean' }];
  it('保留字符串/布尔/数组类型和空值，不把数字或选项静默转字符串', () => {
    const values = { title: '', count: 9007199254740991, date: null, choices: ['1', 'true'], enabled: false };
    expect(convertVFormValues(bindings, values, false)).toEqual(values);
    expect(convertVFormValues(bindings, values, true)).toEqual(values);
    expect(convertVFormValues(bindings, { count: '', date: '' }, true)).toEqual({ count: null, date: null });
  });
  it.each([{ count: 9007199254740992 }, { count: '123' }, { choices: [1] }, { enabled: 'true' }, { title: 1 }])('拒绝不能无损编解码的值 %o', values => {
    expect(() => convertVFormValues(bindings, values, true)).toThrow('client.vform3_value_codec_mismatch');
  });
  it('只消费当前实际运行产物及唯一显式绑定，丢弃未绑定回传键', () => {
    const json = { fullNetRuntime: { rendererHash: 'current', bindingVersion: 1, bindings } };
    expect(readVFormBindings(json, 'current')).toEqual(bindings);
    expect(() => readVFormBindings(json, 'old')).toThrow('client.vform3_runtime_mismatch');
    expect(convertVFormValues(bindings, { title: 'ok', secret: 'not-bound' }, true)).toEqual({ title: 'ok' });
    expect(() => readVFormBindings({ fullNetRuntime: { rendererHash: 'current', bindingVersion: 1, bindings: [bindings[0], bindings[0]] } }, 'current')).toThrow('client.invalid_vform_binding');
  });
});
