import React, { useEffect, useState } from 'react';
import { initiateLogin, checkSession, getUserInfo, logout, UserInfo } from './services/auth';
import ReportPage from './components/ReportPage';

const App: React.FC = () => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [userInfo, setUserInfo] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const init = async () => {
      const auth = await checkSession();
      if (auth) {
        const info = await getUserInfo();
        if (info) {
          setUserInfo(info);
          setIsAuthenticated(true);
        }
      }
      setLoading(false);
    };
    init();
  }, []);

  if (loading) return <div className="App">Загрузка...</div>;

  if (!isAuthenticated) {
    return (
      <div className="App">
        <h1>BionicPRO</h1>
        <button onClick={initiateLogin}>
          Войти через bionicpro-auth
        </button>
      </div>
    );
  }

  const hasProtheticRole = userInfo?.roles?.includes('prothetic_user') ?? false;

  return (
    <div className="App">
      <header>
        <h1>Привет, {userInfo?.email}</h1>
        <button onClick={async () => {
          await logout();
          setIsAuthenticated(false);
          setUserInfo(null);
        }}>
          Выйти
        </button>
      </header>
      {hasProtheticRole && userInfo?.email && <ReportPage userId={userInfo.email} />}
    </div>
  );
};

export default App;