import type { CurrentUserResponse } from '@fullnet/client-contracts';

export type IdentitySessionState = 'initializing' | 'authenticated' | 'anonymous';

export interface IdentitySessionSnapshot {
  readonly state: IdentitySessionState;
  readonly currentUser?: CurrentUserResponse;
}

/** 跨端身份会话控制器；访问令牌只保存在内存中。 */
export interface IdentitySessionController {
  login(username: string, password: string): Promise<void>;
  restore(): Promise<boolean>;
  logout(): Promise<void>;
  can(permission: string): boolean;
  readAccessToken(): string | undefined;
  snapshot(): IdentitySessionSnapshot;
  subscribe(listener: (snapshot: IdentitySessionSnapshot) => void): () => void;
  dispose(): void;
}
