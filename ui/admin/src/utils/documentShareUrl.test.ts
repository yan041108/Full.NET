import { describe, expect, it } from 'vitest';
import { buildDocumentShareUrl } from './documentShareUrl';

describe('buildDocumentShareUrl', () => {
  it('builds hash route from current page origin', () => {
    const url = buildDocumentShareUrl('abc123');
    expect(url).toContain('#/document/share/abc123');
    expect(url.startsWith('http')).toBe(true);
  });

  it('returns empty for blank code', () => {
    expect(buildDocumentShareUrl('')).toBe('');
  });
});
