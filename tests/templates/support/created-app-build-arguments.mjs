/** 将已准入的 CPU 预算显式传给 MSBuild；仅设置运行时处理器数不能限制其工作节点。 */
export function createdAppBuildArguments(args, env) {
  if (args[0] !== 'build') return args;
  const cpu = env.DOTNET_PROCESSOR_COUNT;
  if (cpu === undefined) return args;
  if (!/^[1-9]\d*$/u.test(cpu) || !Number.isSafeInteger(Number(cpu))) {
    throw new Error('独立应用构建的 CPU 预算必须为正整数。');
  }
  return [...args, `-maxcpucount:${cpu}`];
}
