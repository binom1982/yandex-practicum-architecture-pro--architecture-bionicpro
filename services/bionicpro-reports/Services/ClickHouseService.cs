using ClickHouse.Client.ADO;

namespace bionicpro_reports.Services;

public class ClickHouseService
{
    private readonly string _connStr;

    public ClickHouseService()
    {
        _connStr = Environment.GetEnvironmentVariable("CLICKHOUSE_CONN")
            ?? "Host=clickhouse;Port=8123;Database=reports_db;Username=default;Password=";
    }

    public async Task<List<ReportRow>> GetReportAsync(string userId, DateTime? from = null, DateTime? to = null)
    {
        using var connection = new ClickHouseConnection(_connStr);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();

        // Базовый запрос с обязательным условием по user_id
        var sql = @"
            SELECT user_id, report_date, telemetry_json, crm_data_json 
            FROM reports_db.reports_vitrina 
            WHERE user_id = @userId";

        var param = cmd.CreateParameter();
        param.ParameterName = "@userId";
        param.Value = userId;
        cmd.Parameters.Add(param);

        // Опциональная фильтрация по нижней границе периода
        if (from.HasValue)
        {
            sql += " AND report_date >= @fromDate";
            var pFrom = cmd.CreateParameter();
            pFrom.ParameterName = "@fromDate";
            pFrom.Value = from.Value.Date;
            cmd.Parameters.Add(pFrom);
        }

        // Опциональная фильтрация по верхней границе периода
        if (to.HasValue)
        {
            sql += " AND report_date <= @toDate";
            var pTo = cmd.CreateParameter();
            pTo.ParameterName = "@toDate";
            pTo.Value = to.Value.Date;
            cmd.Parameters.Add(pTo);
        }

        sql += " ORDER BY report_date DESC";
        cmd.CommandText = sql;

        var result = new List<ReportRow>();
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new ReportRow
            {
                UserId = reader.GetString(0),
                ReportDate = reader.GetDateTime(1),
                Telemetry = reader.GetString(2),
                CrmData = reader.GetString(3)
            });
        }

        return result;
    }

    public async Task<DateTime?> GetLastProcessedDateAsync()
    {
        using var connection = new ClickHouseConnection(_connStr);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT max(last_processed_date) FROM reports_db.etl_audit WHERE dag_id = 'reports_etl_dag'";

        var result = await cmd.ExecuteScalarAsync();

        if (result == null || result is DBNull) return null;
        return Convert.ToDateTime(result);
    }
}

// DTO для строки отчёта
public class ReportRow
{
    public string UserId { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public string Telemetry { get; set; } = string.Empty;
    public string CrmData { get; set; } = string.Empty;
}