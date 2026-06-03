CREATE DATABASE IF NOT EXISTS reports_db;

CREATE TABLE IF NOT EXISTS reports_db.etl_audit (
    dag_id String,
    last_processed_date DateTime,
    status String,
    updated_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (dag_id, updated_at);

CREATE TABLE IF NOT EXISTS reports_db.reports_vitrina (
    user_id String,
    report_date Date,
    telemetry_json String,
    crm_data_json String,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (user_id, report_date);

-- ✅ SELECT вместо VALUES (обход бага парсера init-скриптов)
INSERT INTO reports_db.etl_audit (dag_id, last_processed_date, status)
SELECT 'reports_etl_dag', toDateTime('2026-06-02 12:00:00'), 'success';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', toDate('2026-06-01'), 'steps=1240;grip=4.2;battery=87', 'orders=2;fit=2026-05-15';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', toDate('2026-06-02'), 'steps=2150;grip=5.1;battery=92', 'orders=2;fit=2026-05-15';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT '5d6fe252-40ba-465e-9a73-8fc63e407631', toDate('2026-06-03'), 'steps=1890;grip=4.8;battery=78', 'orders=3;fit=2026-05-15';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT '3d64636b-1ed2-4b7d-9c4e-3cc887cb4ddf', toDate('2026-06-01'), 'steps=980;grip=3.9;battery=95', 'orders=1;fit=2026-05-20';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT '3d64636b-1ed2-4b7d-9c4e-3cc887cb4ddf', toDate('2026-06-03'), 'steps=1560;grip=4.5;battery=81', 'orders=1;fit=2026-05-20';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT 'a80e1d99-8ac3-474c-9c0e-596eea1b44a1', toDate('2026-06-02'), 'steps=2340;grip=5.6;battery=68', 'orders=4;fit=2026-05-10';

INSERT INTO reports_db.reports_vitrina (user_id, report_date, telemetry_json, crm_data_json)
SELECT 'a80e1d99-8ac3-474c-9c0e-596eea1b44a1', toDate('2026-06-03'), 'steps=1720;grip=4.9;battery=73', 'orders=5;fit=2026-05-10';

-- ✅ Сообщение для отладки
SELECT '✅ ClickHouse init: tables reports_vitrina and etl_audit created successfully' AS init_status;