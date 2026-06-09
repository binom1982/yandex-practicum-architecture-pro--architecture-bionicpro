## Обновленная инструкция

### Задание 1

```
docker-compose down -v
docker-compose up -d

# Ждем 2 минуты пока запуститься keycloak (через health настроить, корректно не получитлось, поэтому ждем)
```

Аутентификация

```
Открываем в браузере frontend 
http://localhost:3000

Фронтенд обращается API Gateway (bionicpro-auth) по адресу http://localhost:8000, а он уже в свою очередь к keycloak

```

![image](screenshots\task1_01_frontend.png)
![image](screenshots\task1_02_keycloak_login.png)
![image](screenshots\task1_03_keycloak_mobile_auth.png)
![image](screenshots\task1_04_frontend_logined.png)

### Задание 2

У меня не получилось добиться создания таблиц в clickhouse при запуске  контейнера.

Пути в докере прописаны, докер с дисками удалял, но скрипты не подхватывает, поэтому их необходимо выполнить после запуска контейнера и наполнить тестовыми данными

```
    volumes:
      - clickhouse-data:/var/lib/clickhouse
      - ./clickhouse/init:/docker-entrypoint-initdb.d
```

Запустить скрипты

```
clickhouse\init\01_create_vitrina.sql
clickhouse\init\02_cdc_pipeline.sql
```

![image](screenshots\task_2_clickhouse_create_tables.png)

```
Открываем в браузере airflow (Он не открывался, тк ему нужно, время, после того как запустится keycloak, airflow должен открыться)
http://localhost:8081/


```

![image](screenshots\task_2_airflow.png)

### Задание 3

Берем отчет из хранилища S3

![image](screenshots\task3_minio_with_bucket.png)

### Задание 4

Скрипты для чтения из kafka и создания MaterializedView для витрины в Clickhouse (я написал, что их нужно запустить в задании 2)

```
clickhouse\init\02_cdc_pipeline.sql
```

![image](screenshots\task4_report.png)

> P.S. В общем, за исключением технических моментов, которые я не смог реализовать, так как домашний компьютер у меня довольно слабый. По данному заданию у меня вопросов нет. Если у Вас не будет критичных замечаний, то прошу зачесть его в таком виде, так как мне уже срочно нужно сдавать следующее))), так как это задание для меня было очень сложным и отняло много времени, но, безусловно, очень интересным и практико-ориентированным.
