interface Binding { fieldKey: string; codec: string; }
/** 值协议只接受已固化绑定；保留 null/空串差别，不对数值或选项作隐式字符串转换。 */
export function readVFormBindings(json: Record<string, unknown>, runtimeHash: string): readonly Binding[] {
  const publication = json.fullNetRuntime as { rendererHash?: string; bindingVersion?: number; bindings?: Binding[] } | undefined;
  if (publication?.rendererHash !== runtimeHash || publication.bindingVersion !== 1 || !Array.isArray(publication.bindings)) throw new Error('client.vform3_runtime_mismatch');
  const keys = new Set<string>();
  for (const binding of publication.bindings) {
    if (!binding || typeof binding.fieldKey !== 'string' || !/^[A-Za-z][A-Za-z0-9_.-]{0,63}$/.test(binding.fieldKey) || ['__proto__', 'prototype', 'constructor'].includes(binding.fieldKey.toLowerCase()) || keys.has(binding.fieldKey) ||
        !['string', 'safe-integer', 'boolean', 'string-array', 'iso-date', 'iso-time'].includes(binding.codec)) throw new Error('client.invalid_vform_binding');
    keys.add(binding.fieldKey);
  }
  return publication.bindings;
}
export function convertVFormValues(bindings: readonly Binding[], values: Readonly<Record<string, unknown>>, toBusiness: boolean): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const binding of bindings) {
    if (!Object.hasOwn(values, binding.fieldKey)) continue;
    let value = values[binding.fieldKey];
    // 原版时间值固定携带秒；分钟精度的业务值显式补零，已有秒值不能截断。
    if (!toBusiness && binding.codec === 'iso-time' && typeof value === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value)) value += ':00';
    // 原版日期、时间和数字清空返回空串，业务空值协议显式编码为 null；文本空串原样保留。
    if (toBusiness && value === '' && ['safe-integer', 'iso-date', 'iso-time'].includes(binding.codec)) value = null;
    const valid = value === null || (binding.codec === 'string' && typeof value === 'string') ||
      (binding.codec === 'safe-integer' && Number.isSafeInteger(value)) ||
      (binding.codec === 'boolean' && typeof value === 'boolean') ||
      (binding.codec === 'string-array' && Array.isArray(value) && value.every(item => typeof item === 'string')) ||
      (binding.codec === 'iso-date' && typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) ||
      (binding.codec === 'iso-time' && typeof value === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d$/.test(value));
    if (!valid) throw new Error('client.vform3_value_codec_mismatch');
    result[binding.fieldKey] = structuredClone(value);
  }
  return result;
}
