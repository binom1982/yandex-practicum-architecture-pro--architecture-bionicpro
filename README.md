# Инструкция

Запустите

```bash
docker-compose down

# Запуск всех сервисов
docker-compose up -d --build

# Проверка статусов
docker-compose ps

# Логи bionicpro-auth
docker-compose logs -f bionicpro-auth
docker compose logs keycloak

# Тест health-эндпоинта
curl http://localhost:8000/health
# Ответ: {"status":"healthy"}
```

Откройте

```
http://localhost:3000
```

1. Нажмите «Войти» → редирект на Keycloak → логин → возврат с  `?code=...`
2. В консоли браузера: `sessionStorage.getItem('access_token')` должен вернуть JWT
