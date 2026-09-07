import { messageKeys, type MessageKey } from '@fullnet/admin-i18n';

/** 将服务端状态或目录提供的动态键限制在已登记词条中，未知值保留为文本。 */
export function translateRuntimeMessage(
  translate: (key: MessageKey) => string,
  key: string
): string {
  const registered = messageKeys.find(candidate => candidate === key);
  return registered === undefined ? key : translate(registered);
}
