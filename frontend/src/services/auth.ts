const AUTH_URL = process.env.REACT_APP_AUTH_URL;

export const initiateLogin = () => {
  window.location.href = `${AUTH_URL}/auth/login?redirect=${window.location.origin}`;
}

export const checkSession = async (): Promise<boolean> => {
  try {
    const res = await fetch(`${AUTH_URL}/auth/session`, { credentials: 'include' });
    return res.ok;
  } catch {
    return false;
  }
};

// 🔹 Исправлено: тип соответствует реальному ответу бэкенда
export interface UserInfo {
  sub: string;
  preferredUsername?: string;
  email?: string;
  roles?: string[]; // ← ? означает опциональное поле
}

export const getUserInfo = async (): Promise<UserInfo | null> => {
  try {
    // 🔹 Один запрос, без дублирования
    const res = await fetch(`${AUTH_URL}/auth/me`, { credentials: 'include' });
    
    if (!res.ok) {
      console.warn('getUserInfo failed:', res.status);
      return null;
    }
    
    return await res.json();
  } catch (error) {
    console.error('getUserInfo error:', error);
    return null;
  }
};

export const logout = async () => {
  try {
    await fetch(`${AUTH_URL}/auth/logout`, { 
      method: 'POST', 
      credentials: 'include' 
    });
  } catch (error) {
    console.error('Logout error:', error);
  }
};