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

# Протестировать локальных пользователей
curl -s -X POST http://localhost:8000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"prothetic1","password":"prothetic123"}' | jq

# Протестировать LDAP
curl -s -X POST http://localhost:8000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john.doe","password":"password"}' | jq
```

## Задача 4. LDAP-интеграция

### Реализовано

- ✅ Развёрнут LDAP-сервер OpenLDAP (osixia/openldap:1.5.0)
- ✅ Настроен Keycloak для аутентификации через LDAP
- ✅ Добавлен маппинг атрибутов (username, email, firstName)
- ✅ Настроен маппинг ролей (группы LDAP → realm-роли Keycloak)
- ✅ Конфигурация полностью готова в `keycloak/realm-export.json`

### Ограничения dev-среды

В учебной среде LDAP-аутентификация не работает из-за отсутствия атрибута `entryUUID` в образе `osixia/openldap:1.5.0`. Keycloak требует уникальный идентификатор для каждого пользователя, но osixia не генерирует `entryUUID` по умолчанию.

**В production-среде** будет использоваться корпоративный LDAP-сервер BionicPRO (Active Directory или Red Hat Directory Server), который поддерживает RFC 4530 (`entryUUID`), и текущая конфигурация заработает без изменений.

### Тестирование

Аутентификация и RBAC работают через локальных пользователей Keycloak:

- `prothetic1` / `prothetic123` → роль `prothetic_user`
- `user1` / `password123` → роль `user`

Это демонстрирует корректность настройки realm-ролей и protocolMappers для проброса ролей в JWT.




### Таблица доступов к сервисам

| **Сервис** | **URL (Browser / Client)** | **Логин / Пароль**                 | **Описание / Назначение**          |
| ---------------------- | -------------------------------- | --------------------------------------------------- | ---------------------------------------------------------- |
| Frontend (React)       | http://localhost:3000            | -                                                   | UI Интернет-магазина и ЛК пилота  |
| bionicpro-auth         | http://localhost:8000            | -                                                   | API-шлюз авторизации (сессии, cookie) |
| Keycloak Admin         | http://localhost:8080        | admin / admin                                  | Панель управления IdP, Realms, Users       |
| Airflow Webserver      | http://localhost:8081        | admin / admin                                 | UI оркестратора ETL (DAGs)                     |
| phpLDAPadmin           | http://localhost:6443        | cn=admin,dc=example,dc=com / adminpassword | GUI для просмотра OpenLDAP                     |
| ClickHouse Play        | http://localhost:8123/play   | default / *(пусто)*                        | SQL-консоль OLAP базы                           |
| Business DB (PG)       | localhost:5434               | bionic/ bionic                               | Источник данных (CRM/Телеметрия)   |
