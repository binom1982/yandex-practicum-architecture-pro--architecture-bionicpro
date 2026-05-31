-- Создать БД, если не существует
CREATE DATABASE IF NOT EXISTS reports_db;

-- reports_db.reports_vitrina
CREATE TABLE IF NOT EXISTS reports_db.reports_vitrina
(
    user_id UInt64,
    report_date Date,
    telemetry_json String,
    crm_data String,
    etl_date Date,
    INDEX idx_user_date (user_id, report_date) TYPE minmax GRANULARITY 4
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(report_date)
ORDER BY (user_id, report_date)
TTL report_date + INTERVAL 2 YEAR;

-- Мета-таблица для отслеживания ETL
CREATE TABLE IF NOT EXISTS reports_db.etl_audit
(
    dag_id String,
    last_processed_date Date,
    processed_at DateTime DEFAULT now()
)
ENGINE = ReplacingMergeTree(processed_at)
ORDER BY (dag_id, last_processed_date);

-- ✅ Сообщение для отладки
SELECT '✅ ClickHouse init: tables reports_vitrina and etl_audit created successfully' AS init_status;