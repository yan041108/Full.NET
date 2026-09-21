import { describe, expect, it } from 'vitest';
import {
  formatProblemMessage,
  resolveProblemFeedbackLevel
} from './fullNetMessage';

describe('fullNetMessage', () => {
  it('maps auth and server failures to error', () => {
    expect(resolveProblemFeedbackLevel({
      status: 403,
      code: 'identity.password_change_required',
      title: 'Forbidden'
    })).toBe('error');
    expect(resolveProblemFeedbackLevel({
      status: 500,
      code: 'server.error',
      title: 'Server'
    })).toBe('error');
  });

  it('maps validation and conflict to warning', () => {
    expect(resolveProblemFeedbackLevel({
      status: 400,
      code: 'identity.password_policy',
      title: 'Policy'
    })).toBe('warning');
    expect(resolveProblemFeedbackLevel({
      status: 409,
      code: 'conflict',
      title: 'Conflict'
    })).toBe('warning');
  });

  it('prefers title over detail for user-facing text', () => {
    expect(formatProblemMessage({
      status: 400,
      code: 'identity.password_policy',
      title: '新密码不符合平台密码策略。',
      detail: 'detail'
    })).toBe('新密码不符合平台密码策略。');
  });
});
