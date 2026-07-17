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
});
