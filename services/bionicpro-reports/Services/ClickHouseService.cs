using ClickHouse.Client.ADO;
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

            var sql = @"
                SELECT user_id, report_date, telemetry_json, crm_data_json 
                FROM reports_vitrina
                WHERE user_id = @userId";

            var param = cmd.CreateParameter();
            param.ParameterName = "@userId";
            param.Value = userId;
            cmd.Parameters.Add(param);

            if (from.HasValue)
            {
                sql += " AND report_date >= @fromDate";
                var pFrom = cmd.CreateParameter();
                pFrom.ParameterName = "@fromDate";
                pFrom.Value = from.Value.Date;
                cmd.Parameters.Add(pFrom);
            }

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
            cmd.CommandText = "SELECT max(last_processed_date) FROM etl_audit WHERE dag_id = 'reports_etl_dag'";

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result is DBNull)
            {
                _logger.LogDebug("No ETL audit records found");
                return null;
            }

            var date = Convert.ToDateTime(result);
            _logger.LogDebug("Last processed date: {Date}", date);
            return date;
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