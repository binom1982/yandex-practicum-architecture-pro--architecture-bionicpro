CREATE DATABASE IF NOT EXISTS reports_db;

CREATE TABLE IF NOT EXISTS reports_db.reports_vitrina
(
    user_id          String,
    report_date      Date,
    telemetry_json   String,
    crm_data_json    String,
    created_at       DateTime DEFAULT now()
)
ENGINE = ReplacingMergeTree(created_at)
PARTITION BY toYYYYMM(report_date)
ORDER BY (user_id, report_date);