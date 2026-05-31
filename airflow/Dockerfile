# airflow/Dockerfile
FROM apache/airflow:2.8.1

# Копируем requirements и устанавливаем
COPY requirements.txt /requirements.txt
RUN pip install --no-cache-dir -r /requirements.txt

# Опционально: настройка прав для пользователя airflow
USER root
RUN chown -R airflow:root /requirements.txt
USER airflow