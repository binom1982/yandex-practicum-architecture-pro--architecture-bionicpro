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

Отладка

```bash
# Пересоберите и перезапустите
docker compose up -d --build bionicpro-auth

# Протестируйте полный поток
curl -X POST http://localhost:8000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"user1","password":"password123"}' \
  -c cookies.txt

# Проверьте ключи в Redis
docker compose exec redis redis-cli KEYS "*"

# Запросите /me с ручной кукой
SESSION_ID=$(grep bionicpro_session cookies.txt | awk '{print $7}')
curl -H "Cookie: bionicpro_session=$SESSION_ID" http://localhost:8000/auth/me


docker compose logs -f bionicpro-auth
```

LDAP

```
Доступы
Keycloak Admin Console: http://localhost:8080/admin (admin/admin)
phpLDAPadmin: http://localhost:6443 (cn=admin,dc=example,dc=com / adminpassword)
```

Тест LDAP

```
# Пересоздать Keycloak с обновлённым realm
docker-compose up -d --force-recreate keycloak

# Подождать запуска (~40 сек)
sleep 40

# Протестировать
curl -s -X POST http://localhost:8000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john.doe","password":"password"}' | jq
```
