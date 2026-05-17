import React, { useEffect, useState } from 'react';
import { initiateLogin, handleCallback } from './services/auth';
import ReportPage from './components/ReportPage';

const App: React.FC = () => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.has('code')) {
      handleCallback(params)
        .then(tokens => {
          if (tokens) {
            // Для Задачи 2: временно храним токены в sessionStorage
            // В Задаче 3 это будет перенесено в bionicpro-auth
            sessionStorage.setItem('access_token', tokens.accessToken);
            sessionStorage.setItem('refresh_token', tokens.refreshToken);
            setIsAuthenticated(true);
            window.history.replaceState({}, document.title, '/');
          }
        })
        .catch(console.error);
    } else {
      // Проверяем, есть ли токен от предыдущего входа
      const token = sessionStorage.getItem('access_token');
      if (token) setIsAuthenticated(true);
    }
  }, []);

  if (!isAuthenticated) {
    return (
      <div className="App">
        <h1>BionicPRO</h1>
        <button onClick={() => initiateLogin()}>
          Войти через Keycloak (PKCE)
        </button>
      </div>
    );
  }

  return (
    <div className="App">
      <ReportPage />
      <button onClick={() => {
        sessionStorage.clear();
        setIsAuthenticated(false);
      }}>
        Выйти
      </button>
    </div>
  );
};

export default App;