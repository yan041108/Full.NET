/** 判断实时会话撤销通知是否应清理当前本地会话；忽略伪造或缺失 sessionId 的通知。 */
export function shouldLogoutOnSessionRevoke(
  currentSessionId: string | undefined,
  revokedSessionId: string | undefined
): boolean {
  if (revokedSessionId === undefined || revokedSessionId.length === 0) {
    return false;
  }

  if (currentSessionId === undefined) {
    return true;
  }

  return normalizeSessionId(currentSessionId) === normalizeSessionId(revokedSessionId);
}

function normalizeSessionId(sessionId: string): string {
  return sessionId.trim().toLowerCase();
}