from airflow import DAG
from airflow.operators.python import PythonOperator
from airflow.providers.postgres.hooks.postgres import PostgresHook
from clickhouse_driver import Client
from datetime import datetime, timedelta
import json

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
        vitrina_rows.append({
            'user_id': user_id,
            'report_date': report_date,
            'telemetry_json': telemetry_json,
            'crm_data': json.dumps({
                'full_name': crm[1],
                'email': crm[2],
                'phone': crm[3],
                'contract_no': crm[4]
            }),
            'etl_date': exec_date
        })
    
    # 4. Load в ClickHouse OLAP витрину
    # Используем clickhouse-driver напрямую
    ch_client = Client(
        host='clickhouse',
        port=9000,  # native protocol
        database='reports_db',
        user='default',
        password=''
    )
    
    # Batch insert с именованными параметрами
    ch_client.execute("""
        INSERT INTO reports_db.reports_vitrina 
        (user_id, report_date, telemetry_json, crm_data, etl_date)
        VALUES
    """, vitrina_rows)
    
    # 5. Обновляем мета-таблицу с последней обработанной датой
    ch_client.execute("""
        INSERT INTO reports_db.etl_audit (dag_id, last_processed_date, processed_at)
        VALUES (%(dag_id)s, %(last_date)s, now())
    """, [{'dag_id': 'reports_etl_dag', 'last_date': exec_date}])
    
    ch_client.disconnect()
    
    return {'processed_rows': len(vitrina_rows), 'date': exec_date}

with DAG(
    dag_id='reports_etl_dag',
    default_args=default_args,
    schedule_interval='0 2 * * *',  # ежедневно в 02:00
    start_date=datetime(2026, 1, 1),
    catchup=False,
    tags=['bionicpro', 'reports', 'etl'],
) as dag:
    
    etl_task = PythonOperator(
        task_id='extract_transform_load',
        python_callable=extract_load,
        provide_context=True,
    )