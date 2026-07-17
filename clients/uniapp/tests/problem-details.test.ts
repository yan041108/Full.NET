import { describe, expect, it } from 'vitest';

import { HttpProblem, toProblemPresentation } from '../src/api/problem-details';

describe('ProblemDetails presentation', () => {
  it('prefers stable violation codes with structured arguments for field messages', () => {
    const problem = new HttpProblem({
      status: 400,
      code: 'identity.invalid_profile',
      title: 'Invalid profile.',
      traceId: 'trace-123',
      violations: [{ field: 'locale', code: 'localization.unsupported_locale', arguments: { locale: 'fr-FR' } }]
    });
    const translate = (code: string, arguments_: Readonly<Record<string, unknown>> = {}) =>
      code === 'localization.unsupported_locale'
        ? `Unsupported locale: ${arguments_.locale}`
        : undefined;

    expect(toProblemPresentation(problem, translate)).toEqual({
      title: 'Invalid profile.',
      traceId: 'trace-123',
      fieldMessages: { locale: ['Unsupported locale: fr-FR'] },
      message: undefined
    });
  });

  it('uses a recognized top-level code when no field violation message is available', () => {
    const problem = new HttpProblem({
      status: 409,
      code: 'identity.profile_version_conflict',
      title: 'Profile changed.',
      traceId: 'trace-456'
    });

    expect(toProblemPresentation(problem, code =>
      code === 'identity.profile_version_conflict' ? 'Please reload and try again.' : undefined
    )).toEqual({
      title: 'Profile changed.',
      traceId: 'trace-456',
      fieldMessages: {},
      message: 'Please reload and try again.'
    });
  });

  it('falls back to the top-level code when no violation code is recognized', () => {
    const problem = new HttpProblem({
      status: 400,
      code: 'identity.invalid_profile',
      title: 'Invalid profile.',
      traceId: 'trace-fallback',
      violations: [{ field: 'locale', code: 'validation.unknown', arguments: {} }]
    });

    expect(toProblemPresentation(problem, code =>
      code === 'identity.invalid_profile' ? 'Please review your profile.' : undefined
    )).toEqual({
      title: 'Invalid profile.',
      traceId: 'trace-fallback',
      fieldMessages: {},
      message: 'Please review your profile.'
    });
  });

  it('uses the safe server title for an unknown code without comparing localized text', () => {
    const problem = new HttpProblem({
      status: 500,
      code: 'server.unknown_code',
      title: 'Request could not be completed.',
      traceId: 'trace-789'
    });

    expect(toProblemPresentation(problem, () => undefined)).toEqual({
      title: 'Request could not be completed.',
      traceId: 'trace-789',
      fieldMessages: {},
      message: 'Request could not be completed.'
    });
  });

  it('uses null-prototype dictionaries and forwards only safe argument primitives', () => {
    const rawArguments = Object.create({ inherited: 'ignored' }) as Record<string, unknown>;
    rawArguments.locale = 'fr-FR';
    rawArguments.retries = 3;
    rawArguments.enabled = true;
    rawArguments.empty = null;
    rawArguments.nested = { value: 'dropped' };
    rawArguments.callback = () => 'dropped';
    rawArguments.symbol = Symbol('dropped');
    rawArguments.bigint = 1n;
    Object.defineProperty(rawArguments, '__proto__', { enumerable: true, value: 'safe-key' });

    const problem = new HttpProblem({
      status: 400,
      title: 'Invalid request.',
      violations: [
        { field: '__proto__', code: 'validation.invalid', arguments: rawArguments },
        { field: 'constructor', code: 'validation.invalid', arguments: {} },
        { field: 'toString', code: 'validation.invalid', arguments: {} }
      ]
    });
    let translatedArguments: Readonly<Record<string, unknown>> | undefined;
    const presentation = toProblemPresentation(problem, (_code, arguments_) => {
      translatedArguments ??= arguments_;
      return 'Invalid value.';
    });

    expect(Object.getPrototypeOf(presentation.fieldMessages)).toBeNull();
    expect(presentation.fieldMessages['__proto__']).toEqual(['Invalid value.']);
    expect(presentation.fieldMessages.constructor).toEqual(['Invalid value.']);
    expect(presentation.fieldMessages.toString).toEqual(['Invalid value.']);
    expect(Object.getPrototypeOf(translatedArguments)).toBeNull();
    expect(translatedArguments).toMatchObject({
      locale: 'fr-FR',
      retries: 3,
      enabled: true,
      empty: null
    });
    expect(translatedArguments?.['__proto__']).toBe('safe-key');
    expect(Object.keys(translatedArguments ?? {}).sort()).toEqual([
      '__proto__', 'empty', 'enabled', 'locale', 'retries'
    ]);
    expect(translatedArguments).not.toHaveProperty('inherited');
    expect(translatedArguments).not.toHaveProperty('nested');
    expect(translatedArguments).not.toHaveProperty('callback');
    expect(translatedArguments).not.toHaveProperty('symbol');
    expect(translatedArguments).not.toHaveProperty('bigint');
  });
});
