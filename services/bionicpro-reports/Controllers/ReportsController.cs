using bionicpro_reports.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;

[ApiController]
[Route("reports")]
public class ReportsController : ControllerBase
{
    private readonly ClickHouseService _clickHouse;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ReportsController> _logger;
    private readonly IAmazonS3 _s3Client;

    public ReportsController(
        ClickHouseService clickHouse,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ReportsController> logger,
        IAmazonS3 s3Client)
    {
        _clickHouse = clickHouse;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
        _s3Client = s3Client;
    }

    [HttpGet]
    public async Task<IActionResult> GetReport(
        [FromQuery] string user_id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        // Валидация сессии
        if (!Request.Cookies.TryGetValue(_config["SessionCookieName"] ?? "bionicpro_session", out var sessionId))
        {
            _logger.LogWarning("No session cookie found");
            return Unauthorized();
        }

        var authUrl = _config["AuthServiceUrl"] ?? "http://bionicpro-auth:8080";
        var client = _httpClientFactory.CreateClient();

        var authRequest = new HttpRequestMessage(HttpMethod.Get, $"{authUrl}/auth/me");
        authRequest.Headers.Add("Cookie", $"bionicpro_session={sessionId}");

        var authResponse = await client.SendAsync(authRequest);
        if (!authResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Auth service returned {StatusCode}", authResponse.StatusCode);
            return Unauthorized();
        }

        var userInfo = await authResponse.Content.ReadFromJsonAsync<JsonElement>();
        var currentUserId = userInfo.GetProperty("sub").GetString();
        var userEmail = userInfo.GetProperty("email").GetString();

        if (string.IsNullOrEmpty(currentUserId) || currentUserId != user_id)
        {
            _logger.LogWarning("User {CurrentUserId} tried to access reports for {RequestedUserId}",
                currentUserId, user_id);
            return StatusCode(403, new { error = "Доступ только к собственным отчётам" });
        }

        // Формирование ключа S3
        string fromStr = from?.ToString("yyyy-MM-dd") ?? "start";
        string toStr = to?.ToString("yyyy-MM-dd") ?? "end";
        string s3Key = $"{user_id}/report_{fromStr}_{toStr}.json";

        string bucketName = _config["S3__BucketName"] ?? "reports";
        string cdnBaseUrl = _config["CDN__BaseUrl"] ?? "http://localhost:8888";
        string cdnUrl = $"{cdnBaseUrl}/reports/{s3Key}";

        // Проверка наличия в S3
        bool reportExists = await S3ObjectExistsAsync(bucketName, s3Key);

        if (!reportExists)
        {
            _logger.LogInformation("Report not found in S3, generating from ClickHouse...");

            var report = await _clickHouse.GetReportAsync(user_id, from, to);

            if (report.Count == 0)
                return NotFound(new { message = "Нет данных за указанный период" });

            // 🔹 [CDC] Формирование ответа: адаптивная структура под новую/старую витрину
            var reportData = new
            {
                userId = user_id,
                period = new { from = fromStr, to = toStr },
                // 🔹 [CDC] Если есть новые поля — используем их, иначе — старые
                rows = report.Select(r => new
                {
                    // Новые поля (CDC)
                    client_id = r.UserId,
                    report_date = r.ReportDate,
                    client_name = r.ClientName,
                    total_steps = r.TotalSteps,
                    avg_battery = r.AvgBattery,
                    // Старые поля (LEGACY, для обратной совместимости)
                    telemetry = r.Telemetry,
                    crm_data = r.CrmData
                }).ToList()
            };

            var jsonContent = JsonSerializer.Serialize(reportData);
            var putRequest = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = s3Key,
                ContentBody = jsonContent,
                ContentType = "application/json"
            };

            try
            {
                await _s3Client.PutObjectAsync(putRequest);
                _logger.LogInformation("Report saved to S3: {S3Key}", s3Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save report to S3");
                return StatusCode(500, new { error = "Ошибка сохранения отчёта в хранилище" });
            }
        }
        else
        {
            _logger.LogInformation("Report found in S3, returning CDN link.");
        }

        return Ok(new
        {
            url = cdnUrl,
            cached = reportExists
        });
    }

    private async Task<bool> S3ObjectExistsAsync(string bucketName, string key)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key
            };
            await _s3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking S3 object existence for {Key}", key);
            return false;
        }
    }
}