const config = window.OIDC_RP_CONFIG;
const apiBase = new URLSearchParams(window.location.search).get('api') ?? 'http://localhost:5149';

function base64UrlEncode(bytes) {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/u, '');
}

async function createPkcePair() {
  const verifierBytes = crypto.getRandomValues(new Uint8Array(32));
  const verifier = base64UrlEncode(verifierBytes);
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier));
  const challenge = base64UrlEncode(new Uint8Array(digest));
  return { verifier, challenge };
}

function renderStatus(message, testId) {
  const root = document.getElementById('status');
  root.textContent = message;
  root.dataset.testid = testId ?? '';
}

async function startSignIn(extraParams = {}) {
  const state = crypto.randomUUID().replace(/-/gu, '');
  const nonce = crypto.randomUUID().replace(/-/gu, '');
  const { verifier, challenge } = await createPkcePair();
  sessionStorage.setItem('oidc.pkce.verifier', verifier);
  sessionStorage.setItem('oidc.pkce.state', state);
  sessionStorage.setItem('oidc.pkce.nonce', nonce);
  const params = new URLSearchParams({
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    response_type: 'code',
    scope: 'openid profile',
    state,
    nonce,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    ...extraParams
  });
  window.location.assign(`${apiBase}/connect/authorize?${params.toString()}`);
}

function handleCallback() {
  const params = new URLSearchParams(window.location.search);
  const code = params.get('code');
  const error = params.get('error');
  if (error) {
    renderStatus(error, 'oidc-error');
    return;
  }
  if (!code) {
    renderStatus('ready', 'oidc-ready');
    return;
  }
  const expectedState = sessionStorage.getItem('oidc.pkce.state');
  if (params.get('state') !== expectedState) {
    renderStatus('state_mismatch', 'oidc-state-mismatch');
    return;
  }
  sessionStorage.setItem('oidc.auth.code', code);
  renderStatus('authorized', 'oidc-authorized');
}

document.getElementById('sign-in')?.addEventListener('click', () => {
  void startSignIn();
});
document.getElementById('sign-in-prompt-login')?.addEventListener('click', () => {
  void startSignIn({ prompt: 'login' });
});
document.getElementById('sign-in-prompt-none')?.addEventListener('click', () => {
  void startSignIn({ prompt: 'none' });
});

handleCallback();