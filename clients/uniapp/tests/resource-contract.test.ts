import { describe, expect, it } from 'vitest';

import enUS from '../src/i18n/messages.en-US.json';
import zhCN from '../src/i18n/messages.zh-CN.json';

type MessageTree = string | { readonly [key: string]: MessageTree };

function collectMessages(value: MessageTree, path = ''): Map<string, string> {
  if (typeof value === 'string') {
    return new Map([[path, value]]);
  }

  return Object.entries(value).reduce((messages, [key, child]) => {
    for (const [childPath, message] of collectMessages(child, path ? `${path}.${key}` : key)) {
      messages.set(childPath, message);
    }
    return messages;
  }, new Map<string, string>());
}

describe('uni-app message resource contract', () => {
  it('keeps the Chinese and English resource key sets identical and complete', () => {
    const zhMessages = collectMessages(zhCN);
    const enMessages = collectMessages(enUS);

    expect([...zhMessages.keys()].sort()).toEqual([...enMessages.keys()].sort());
    expect([...zhMessages.keys()]).toEqual(
      expect.arrayContaining([
        'app.name',
        'settings.title',
        'settings.save.saving',
        'settings.save.success',
        'settings.save.failure',
        'errors.localization.unsupported_locale',
        'errors.identity.profile_version_conflict',
        'validation.required',
        'validation.invalid_email',
        'traceId.label'
      ])
    );
  });

  it('contains no blank or placeholder messages', () => {
    for (const [locale, messages] of Object.entries({ 'zh-CN': zhCN, 'en-US': enUS })) {
      for (const [key, message] of collectMessages(messages)) {
        expect(message, `${locale}.${key} must not be blank`).not.toMatch(/^\s*$/);
        expect(message, `${locale}.${key} must not contain a placeholder`).not.toMatch(/\b(?:TODO|TBD)\b/i);
      }
    }
  });
});
