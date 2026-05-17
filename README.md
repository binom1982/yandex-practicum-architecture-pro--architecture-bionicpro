# Инструкция

Запустите

```bash
docker-compose down

docker-compose up -d --build
```

Откройте

```
http://localhost:3000
```

1. Нажмите «Войти» → редирект на Keycloak → логин → возврат с  `?code=...`
2. В консоли браузера: `sessionStorage.getItem('access_token')` должен вернуть JWT
