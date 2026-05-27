import React, { useState } from 'react';

interface Props {
  userId: string;
}

const ReportPage: React.FC<Props> = ({ userId }) => {
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const handleGenerate = async () => {
    try {
      setLoading(true);
      setError(null);

      const response = await fetch(`${process.env.REACT_APP_REPORTS_URL}/reports?user_id=${userId}`, {
        credentials: 'include'
      });

      if (!response.ok) {
        throw new Error('Failed to generate report');
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `report-${userId}-${Date.now()}.pdf`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : 'Ошибка генерации';
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="report-page">
      <h2>Отчёт по протезу</h2>
      <button onClick={handleGenerate} disabled={loading}>
        {loading ? 'Формирование...' : 'Сформировать отчёт'}
      </button>
      {error && <p className="error">{error}</p>}
    </div>
  );
};

export default ReportPage;