const zh = {
  title: '业务写入策略',
  hint: '为创建、更新、删除生成必需的业务策略接口。请在本模块实现并注册；未注册时写入服务无法构造。策略需有 Version 并保持模块本地事务边界。',
  disabled: '请使用显式 entityCapabilities 能力模型并启用 hasVersion 后配置。'
} as const;
const en: Record<keyof typeof zh, string> = {
  title: 'Business mutation policy',
  hint: 'Generate a required business policy for create, update and delete. Implement and register it in this module; write services cannot resolve without it. Version and module-local transaction boundaries are required.',
  disabled: 'Use explicit entityCapabilities with hasVersion enabled to configure this option.'
};
export function codegenMutationPolicyText(locale: string, key: keyof typeof zh) { return (locale === 'en-US' ? en : zh)[key]; }
