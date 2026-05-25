CREATE TABLE IF NOT EXISTS reports_db.etl_audit
(
    dag_id               String,
    last_processed_date  Date,
    processed_at         DateTime
)
ENGINE = ReplacingMergeTree(processed_at)
ORDER BY dag_id;