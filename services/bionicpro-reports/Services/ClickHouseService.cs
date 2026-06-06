using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Parameters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace bionicpro_reports.Services;

public class ClickHouseService
{
    private readonly string _connStr;
    private readonly ILogger<ClickHouseService> _logger;
    private readonly bool _useCdcVitrina; // 🔹 [CDC] Флаг переключения на новую витрину

    public ClickHouseService(IConfiguration config, ILogger<ClickHouseService> logger)
    {
        _connStr = config.GetValue<string>("ClickHouse:ConnectionString")
            ?? throw new InvalidOperationException("ClickHouse connection string is not configured");
        _logger = logger;
        // 🔹 [CDC] Читаем настройку из config (по умолчанию — новая витрина)
        _useCdcVitrina = config.GetValue<bool>("ClickHouse:UseCdcVitrina", true);
    }

    public async Task<List<ReportRow>> GetReportAsync(string userId, DateTime? from = null, DateTime? to = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("GetReportAsync called with empty userId");
            throw new ArgumentException("userId cannot be empty", nameof(userId));
        }

        try
        {
            _logger.LogDebug("Fetching report for user {UserId}, period: {From} - {To}",
                userId, from?.ToString("yyyy-MM-dd"), to?.ToString("yyyy-MM-dd"));

            using var connection = new ClickHouseConnection(_connStr);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();

            // 🔹 [CDC] Хардкод для тестов — можно убрать в продакшене
            // userId = "5d6fe252-40ba-465e-9a73-8fc63e407631";

            var safeUserId = userId.Replace("'", "''");

            // 🔹 [CDC] НОВАЯ ВИТРИНА: ReplacingMergeTree + FINAL + новые колонки
            if (_useCdcVitrina)
            {
                var sql = $@"
                    SELECT client_id, report_date, client_name, total_steps, avg_battery 
                    FROM reports_db.reports_vitrina_cdc FINAL
                    WHERE client_id = '{safeUserId}'";

                if (from.HasValue)
                    sql += $" AND report_date >= '{from.Value:yyyy-MM-dd}'";
                if (to.HasValue)
                    sql += $" AND report_date <= '{to.Value:yyyy-MM-dd}'";

                sql += " ORDER BY report_date DESC";
                cmd.CommandText = sql;

                _logger.LogDebug("Executing CDC query: {Query}", cmd.CommandText);

                var result = new List<ReportRow>();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(new ReportRow
                    {
                        UserId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),      // client_id
                        ReportDate = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1), // report_date
                        ClientName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),  // client_name (NEW)
                        TotalSteps = reader.IsDBNull(3) ? 0 : Convert.ToUInt64(reader.GetValue(3)), // total_steps (NEW)
                        AvgBattery = reader.IsDBNull(4) ? 0 : Convert.ToDouble(reader.GetValue(4))  // avg_battery (NEW)
                    });
                }
                return result;
            }
            // 🔹 [CDC] СТАРАЯ ВИТРИНА: оставлена для отката/сравнения
            else
            {
                var sql = $@"
                    SELECT user_id, report_date, telemetry_json, crm_data_json 
                    FROM reports_vitrina
                    WHERE user_id = '{safeUserId}'";

                if (from.HasValue)
                    sql += $" AND report_date >= '{from.Value:yyyy-MM-dd}'";
                if (to.HasValue)
                    sql += $" AND report_date <= '{to.Value:yyyy-MM-dd}'";

                sql += " ORDER BY report_date DESC";
                cmd.CommandText = sql;

                _logger.LogDebug("Executing LEGACY query: {Query}", cmd.CommandText);

                var result = new List<ReportRow>();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(new ReportRow
                    {
                        UserId = reader.GetString(0),
                        ReportDate = reader.GetDateTime(1),
                        Telemetry = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        CrmData = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                    });
                }
                return result;
            }
        }
        catch (Exception ex) when (
            ex is not ArgumentException &&
            (ex.GetType().Namespace?.StartsWith("ClickHouse") == true || ex.Message.Contains("ClickHouse"))
        )
        {
            _logger.LogError(ex, "ClickHouse-related error for user {UserId}", userId);
            throw new ServiceException($"ClickHouse error: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Unexpected error while fetching report for user {UserId}", userId);
            throw;
        }
    }

    public async Task<DateTime?> GetLastProcessedDateAsync()
    {
        try
        {
            using var connection = new ClickHouseConnection(_connStr);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT max(last_processed_date) 
                FROM system.tables 
                WHERE database = 'reports_db' AND name = 'etl_audit'
            ";

            var tableExists = await cmd.ExecuteScalarAsync();
            if (tableExists == null || tableExists is DBNull)
            {
                _logger.LogWarning("Table reports_db.etl_audit not found. ETL may not have run yet.");
                return null;
            }

            cmd.CommandText = "SELECT max(last_processed_date) FROM reports_db.etl_audit WHERE dag_id = 'reports_etl_dag'";
            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result is DBNull)
            {
                _logger.LogDebug("No ETL audit records found");
                return null;
            }

            return DateTime.Now;
        }
        catch (Exception ex) when (
            ex is not ArgumentException &&
            (ex.GetType().Namespace?.StartsWith("ClickHouse") == true || (ex.Message.Contains("UNKNOWN_DATABASE") || ex.Message.Contains("UNKNOWN_TABLE")))
        )
        {
            _logger.LogWarning("ClickHouse database/tables not initialized yet. ETL may not have run.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching last processed date from ClickHouse");
            throw new ServiceException("Failed to fetch ETL status", ex);
        }
    }
}

public class ServiceException : Exception
{
    public ServiceException(string message) : base(message) { }
    public ServiceException(string message, Exception inner) : base(message, inner) { }
}

public class ReportRow
{
    public string UserId { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }

    // 🔹 [CDC] Поля для новой витрины
    public string ClientName { get; set; } = string.Empty;        // NEW: из CRM CDC
    public ulong TotalSteps { get; set; } = 0;                    // NEW: агрегированные шаги
    public double AvgBattery { get; set; } = 0;                   // NEW: средний заряд

    // 🔹 [CDC] Поля старой витрины (оставлены для обратной совместимости)
    public string Telemetry { get; set; } = string.Empty;         // LEGACY
    public string CrmData { get; set; } = string.Empty;           // LEGACY
}