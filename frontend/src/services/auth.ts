import { generateCodeVerifier, generateCodeChallenge } from '../utils/pkce';

// Используем процесс.env.REACT_APP_* (Create React App convention)
const KEYCLOAK_URL = process.env.REACT_APP_KEYCLOAK_URL || '';
const REALM = process.env.REACT_APP_KEYCLOAK_REALM || '';
const CLIENT_ID = process.env.REACT_APP_KEYCLOAK_CLIENT_ID || '';
const REDIRECT_URI = window.location.origin;

export const initiateLogin = async (): Promise<void> => {
  const codeVerifier = generateCodeVerifier();
  const codeChallenge = await generateCodeChallenge(codeVerifier);
  const state = crypto.randomUUID();

  sessionStorage.setItem('pkce_code_verifier', codeVerifier);
  sessionStorage.setItem('pkce_state', state);

  const authUrl = `${KEYCLOAK_URL}/realms/${REALM}/protocol/openid-connect/auth?` +
    `client_id=${CLIENT_ID}&` +
    `redirect_uri=${encodeURIComponent(REDIRECT_URI)}&` +
    `response_type=code&` +
    `scope=openid profile email&` +
    `code_challenge=${codeChallenge}&` +
    `code_challenge_method=S256&` +
    `state=${state}`;

  window.location.href = authUrl;
};

export const handleCallback = async (searchParams: URLSearchParams): Promise<{ accessToken: string; refreshToken: string } | null> => {
  const code = searchParams.get('code');
  const state = searchParams.get('state');
  const savedState = sessionStorage.getItem('pkce_state');
  const codeVerifier = sessionStorage.getItem('pkce_code_verifier');

  if (!code || !state || state !== savedState || !codeVerifier) {
    console.error('PKCE validation failed or state mismatch');
    return null;
  }

  const response = await fetch(`${KEYCLOAK_URL}/realms/${REALM}/protocol/openid-connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: CLIENT_ID,
      redirect_uri: REDIRECT_URI,
      code,
      code_verifier: codeVerifier,
    }),
  });

  if (!response.ok) throw new Error('Token exchange failed');

  const tokens = await response.json();
  sessionStorage.removeItem('pkce_code_verifier');
  sessionStorage.removeItem('pkce_state');

  return { accessToken: tokens.access_token, refreshToken: tokens.refresh_token };
};