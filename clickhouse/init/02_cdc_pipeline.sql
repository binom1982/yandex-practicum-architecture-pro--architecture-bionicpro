
CREATE DATABASE IF NOT EXISTS reports_db;

-- 1. Целевая таблица для CRM-клиентов (CDC)
CREATE TABLE IF NOT EXISTS reports_db.crm_clients_cdc (
    client_id String,
    full_name String,
    email String,
    phone String,
    _version UInt64 DEFAULT now64()
) ENGINE = ReplacingMergeTree(_version)
ORDER BY client_id;

-- 2. KafkaEngine: читает топик Debezium
CREATE TABLE IF NOT EXISTS reports_db.crm_clients_kafka (
    `payload.after.client_id` String,
    `payload.after.full_name` String,
    `payload.after.email` String,
    `payload.after.phone` String,
    `payload.op` String
) ENGINE = Kafka
SETTINGS
    kafka_broker_list = 'kafka:9092',
    kafka_topic_list = 'crm.cdc.public.clients',
    kafka_group_name = 'ch-cdc-clients-consumer',
    kafka_format = 'JSONEachRow',
    kafka_num_consumers = 1,
    input_format_import_nested_json = 1,
    kafka_skip_broken_messages = 10;

-- 3. MV: трансформирует Kafka-JSON → целевая таблица
CREATE MATERIALIZED VIEW IF NOT EXISTS reports_db.crm_clients_mv
TO reports_db.crm_clients_cdc AS
SELECT
    `payload.after.client_id` AS client_id,
    `payload.after.full_name` AS full_name,
    `payload.after.email` AS email,
    `payload.after.phone` AS phone,
    toUInt64(now64()) AS _version
FROM reports_db.crm_clients_kafka
WHERE `payload.op` IN ('c', 'u');

-- 4. Таблица телеметрии (симуляция потока с датчиков)
CREATE TABLE IF NOT EXISTS reports_db.telemetry_raw (
    client_id String,
    event_ts DateTime,
    steps UInt32,
    battery Float64,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree() ORDER BY (client_id, event_ts);

-- 5. Финальная витрина для API отчётов
CREATE TABLE IF NOT EXISTS reports_db.reports_vitrina_cdc (
    client_id String,
    report_date Date,
    client_name String,
    total_steps UInt64,
    avg_battery Float64,
    updated_at DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(updated_at)
ORDER BY (client_id, report_date);

-- 6. MV: автоагрегация телеметрии + CRM
CREATE MATERIALIZED VIEW IF NOT EXISTS reports_db.reports_vitrina_mv
TO reports_db.reports_vitrina_cdc AS
SELECT
    t.client_id,
    toDate(t.event_ts) AS report_date,
    any(c.full_name) AS client_name,
    sum(t.steps) AS total_steps,
    round(avg(t.battery), 2) AS avg_battery,
    now() AS updated_at
FROM reports_db.telemetry_raw AS t
ANY LEFT JOIN reports_db.crm_clients_cdc AS c ON t.client_id = c.client_id
GROUP BY t.client_id, report_date;

-- ─────────────────────────────────────────────────────
-- 🧪 СИМУЛЯЦИЯ: вставляем данные напрямую, как будто Kafka их уже доставила
-- ─────────────────────────────────────────────────────
INSERT INTO reports_db.crm_clients_cdc (client_id, full_name, email, phone)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', 'Иван Петров', 'ivan@example.com', '+79001112233';

INSERT INTO reports_db.crm_clients_cdc (client_id, full_name, email, phone)
SELECT '3d64636b-1ed2-4b7d-9c4e-3cc887cb4ddf', 'Мария Сидорова', 'maria@example.com', '+79004445566';

INSERT INTO reports_db.telemetry_raw (client_id, event_ts, steps, battery)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', toDateTime('2026-06-01 10:00:00'), 1240, 87.5;

INSERT INTO reports_db.telemetry_raw (client_id, event_ts, steps, battery)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', toDateTime('2026-06-02 14:30:00'), 2150, 92.1;

INSERT INTO reports_db.telemetry_raw (client_id, event_ts, steps, battery)
SELECT '3d64636b-1ed2-4b7d-9c4e-3cc887cb4ddf', toDateTime('2026-06-01 09:15:00'), 980, 95.0;

INSERT INTO reports_db.telemetry_raw (client_id, event_ts, steps, battery)
SELECT '3d64636b-1ed2-4b7d-9c4e-3cc887cb4ddf', toDateTime('2026-06-03 18:00:00'), 1560, 81.3;

-- ⏳ MV отработает асинхронно. Дадим 2 секунды на обработку
SELECT sleep(2);

-- ✅ Проверка витрины
SELECT 
    client_id, 
    report_date, 
    client_name, 
    total_steps, 
    avg_battery 
FROM reports_db.reports_vitrina_cdc 
FINAL 
ORDER BY client_id, report_date;

SELECT '✅ Этап 2 завершён: CDC-пайплайн и витрина готовы' AS status;