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

      // 1. Запрашиваем ссылку на отчёт у API (с передачей сессионной cookie)
      const response = await fetch(
        `${process.env.REACT_APP_REPORTS_URL}/reports?user_id=${userId}`,
        { credentials: 'include' }
      );

      if (!response.ok) {
        // 🔹 Читаем сообщение об ошибке из ответа сервера
        let serverMessage = 'Failed to generate report';
        try {
          const errorData = await response.json();
          if (errorData?.error) {
            serverMessage = errorData.error;
          }
          if (errorData?.retryAfter) {
            serverMessage += ` (повторите после ${errorData.retryAfter})`;
          }
        } catch {
          // Если ответ не JSON — оставляем дефолтное сообщение
        }
        throw new Error(serverMessage);
      }

      // 🔹 ИЗМЕНЕНО: Получаем JSON с URL на CDN вместо сырых данных
      const data = await response.json();
      const cdnUrl = data.url;

      if (!cdnUrl) {
        throw new Error('API не вернул ссылку на отчёт');
      }

      // 🔹 ИЗМЕНЕНО: Скачиваем файл напрямую из CDN
      // Используем fetch для получения Blob, чтобы браузер гарантированно скачал файл,
      // а не просто отобразил JSON текст в новой вкладке.
      // Запрос к CDN идет без credentials, так как Nginx отдает файлы анонимно.
      const fileResponse = await fetch(cdnUrl);
      if (!fileResponse.ok) {
        throw new Error(`Ошибка скачивания из CDN: ${fileResponse.status}`);
      }

      const blob = await fileResponse.blob();
      const blobUrl = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = blobUrl;
      
      // Берём имя файла из URL CDN (обрезаем query-параметры типа ?v=123) или генерируем дефолтное
      const urlFileName = cdnUrl.split('/').pop()?.split('?')[0];
      a.download = urlFileName || `report-${userId}.json`;
      
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(blobUrl);

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