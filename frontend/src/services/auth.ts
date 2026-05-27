const AUTH_URL = process.env.REACT_APP_AUTH_URL;

export const initiateLogin = () => {
  window.location.href = `${AUTH_URL}/auth/login?redirect=${window.location.origin}`;
};

export const checkSession = async (): Promise<boolean> => {
  try {
    const res = await fetch(`${AUTH_URL}/auth/session`, { credentials: 'include' });
    return res.ok;
  } catch {
    return false;
  }
};

export const getUserInfo = async (): Promise<{ email: string; roles: string[] } | null> => {
  try {
    const res = await fetch(`${AUTH_URL}/auth/me`, { credentials: 'include' });
    if (!res.ok) return null;
    return await res.json();
  } catch {
    return null;
  }
};

export const logout = async () => {
  await fetch(`${AUTH_URL}/auth/logout`, { 
    method: 'POST', 
    credentials: 'include' 
  });
};