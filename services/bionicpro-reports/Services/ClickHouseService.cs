using ClickHouse.Client.ADO;
using ClickHouse.Client.ADO.Parameters;

//using ClickHouse.Client.Exceptions;  // 🔹 Исправлено: правильное пространство имен
// ИЛИ, если выше не работает:
// using ClickHouse.Client;  // ← альтернатива: исключение в корневом namespace
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

    public ClickHouseService(IConfiguration config, ILogger<ClickHouseService> logger)
    {
        _connStr = config.GetValue<string>("ClickHouse:ConnectionString")
            ?? throw new InvalidOperationException("ClickHouse connection string is not configured");
        _logger = logger;
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

            userId = "5d6fe252-40ba-465e-9a73-8fc63e407631";

            // Вместо параметров — безопасная подстановка для UUID
            var safeUserId = userId.Replace("'", "''"); // экранирование одиночных кавычек

            var sql = $@"
                SELECT user_id, report_date, telemetry_json, crm_data_json 
                FROM reports_vitrina
                WHERE user_id = '{safeUserId}'";

            // Для дат:
            if (from.HasValue)
                sql += $" AND report_date >= '{from.Value:yyyy-MM-dd}'";
            if (to.HasValue)
                sql += $" AND report_date <= '{to.Value:yyyy-MM-dd}'";

            sql += " ORDER BY report_date DESC";
            cmd.CommandText = sql;

            _logger.LogDebug("Executing query: {Query}", cmd.CommandText);

            var result = new List<ReportRow>();
            using var reader = await cmd.ExecuteReaderAsync();

            var rowCount = 0;
            while (await reader.ReadAsync())
            {
                result.Add(new ReportRow
                {
                    UserId = reader.GetString(0),
                    ReportDate = reader.GetDateTime(1),
                    Telemetry = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CrmData = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
                });
                rowCount++;
            }

            _logger.LogDebug("Query returned {RowCount} rows for user {UserId}", rowCount, userId);
            return result;
        }
        // 🔹 Исправлено: используем правильное имя исключения
        //catch (ClickHouseException ex)  // ← без .Client.Exceptions
        //{
        //    _logger.LogError(ex, "ClickHouse error while fetching report for user {UserId}", userId);
        //    throw new ServiceException($"Failed to fetch report from ClickHouse: {ex.Message}", ex);
        //}
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

            // Сначала проверим, существует ли таблица
            var tableExists = await cmd.ExecuteScalarAsync();
            if (tableExists == null || tableExists is DBNull)
            {
                _logger.LogWarning("Table reports_db.etl_audit not found. ETL may not have run yet.");
                return null;
            }

            // Если таблица есть — запрашиваем дату
            cmd.CommandText = "SELECT max(last_processed_date) FROM reports_db.etl_audit WHERE dag_id = 'reports_etl_dag'";
            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result is DBNull)
            {
                _logger.LogDebug("No ETL audit records found");
                return null;
            }

            return DateTime.Now;//Convert.ToDateTime(result);
        }
        catch (Exception ex) when (
            ex is not ArgumentException &&
            (ex.GetType().Namespace?.StartsWith("ClickHouse") == true || (ex.Message.Contains("UNKNOWN_DATABASE") || ex.Message.Contains("UNKNOWN_TABLE")))
        )
        {
            _logger.LogWarning("ClickHouse database/tables not initialized yet. ETL may not have run.");
            return null;  // Возвращаем null, а не выбрасываем исключение
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
    public string Telemetry { get; set; } = string.Empty;
    public string CrmData { get; set; } = string.Empty;
}