import { describe, expect, it } from 'vitest';
import {
  areEquivalentPreV1ErrorCodes,
  normalizePreV1ErrorCode,
} from '../src/pre-v1-protocol';

describe('pre-v1 protocol compatibility', () => {
  it('normalizePreV1ErrorCode maps registered legacy tenancy codes', () => {
    expect(normalizePreV1ErrorCode('tenancy.identifier-exists'))
      .toBe('tenancy.identifier_exists');
    expect(normalizePreV1ErrorCode('tenancy.identifier_exists'))
      .toBe('tenancy.identifier_exists');
  });

  it('areEquivalentPreV1ErrorCodes treats legacy and canonical as equal', () => {
    expect(areEquivalentPreV1ErrorCodes(
      'tenancy.domain-exists',
      'tenancy.domain_exists',
    )).toBe(true);
    expect(areEquivalentPreV1ErrorCodes(
      'identity.invalid_credentials',
      'identity.invalid_credentials',
    )).toBe(true);
  });
});
