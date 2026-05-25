from airflow import DAG
from airflow.operators.python import PythonOperator
from airflow.providers.postgres.hooks.postgres import PostgresHook
from airflow.providers.clickhouse.hooks.clickhouse import ClickHouseHook  # или clickhouse-driver
from datetime import datetime, timedelta

default_args = {
    'owner': 'bionicpro',
    'retries': 2,
    'retry_delay': timedelta(minutes=5),
}

def extract_load(**ctx):
    exec_date = ctx['ds']  # дата запуска DAG (YYYY-MM-DD)
    
    # 1. Extract из CRM DB (персональные данные клиентов)
    crm = PostgresHook(postgres_conn_id='crm_db')
    crm_rows = crm.get_records("""
        SELECT user_id, full_name, email, phone, contract_no
        FROM clients
        WHERE updated_at::date <= %s
    """, parameters=(exec_date,))
    
    # 2. Extract telemetry (агрегация по пользователям за день)
    tel = PostgresHook(postgres_conn_id='telemetry_db')
    tel_rows = tel.get_records("""
        SELECT user_id, 
               date_trunc('day', ts)::date AS report_date,
               json_agg(json_build_object(
                   'ts', ts, 'grip', grip, 'battery', battery, 'signal', signal_quality
               )) AS telemetry_json
        FROM prosthetic_telemetry
        WHERE ts::date = %s
        GROUP BY user_id, date_trunc('day', ts)::date
    """, parameters=(exec_date,))
    
    # 3. Transform: join по user_id
    crm_dict = {r[0]: r for r in crm_rows}
    vitrina_rows = []
    for user_id, report_date, telemetry_json in tel_rows:
        crm = crm_dict.get(user_id, (user_id, 'N/A', 'N/A', 'N/A', 'N/A'))
        vitrina_rows.append((
            user_id, report_date, telemetry_json,
            f'{{"full_name":"{crm[1]}","email":"{crm[2]}","phone":"{crm[3]}","contract_no":"{crm[4]}"}}',
            exec_date
        ))
    
    # 4. Load в ClickHouse OLAP витрину
    ch = ClickHouseHook(clickhouse_conn_id='clickhouse_olap')
    ch.run("INSERT INTO reports_db.reports_vitrina VALUES", vitrina_rows)
    
    # 5. Обновляем мета-таблицу с последней обработанной датой (для API-проверки)
    ch.run(f"""
        INSERT INTO reports_db.etl_audit (dag_id, last_processed_date, processed_at)
        VALUES ('reports_etl_dag', '{exec_date}', now())
    """)

with DAG(
    dag_id='reports_etl_dag',
    default_args=default_args,
    schedule_interval='0 2 * * *',       # ежедневно в 02:00
    start_date=datetime(2026, 1, 1),
    catchup=False,
    tags=['bionicpro', 'reports', 'etl'],
) as dag:
    
    etl_task = PythonOperator(
        task_id='extract_transform_load',
        python_callable=extract_load,
    )