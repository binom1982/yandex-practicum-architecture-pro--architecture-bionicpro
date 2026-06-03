using bionicpro_reports.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;

[ApiController]
[Route("reports")]
public class ReportsController : ControllerBase
{
    private readonly ClickHouseService _clickHouse;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        ClickHouseService clickHouse,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ReportsController> logger)
    {
        _clickHouse = clickHouse;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetReport(
        [FromQuery] string user_id,  // ← должен быть UUID (sub), а не email
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        // Валидация сессии через вызов к auth-сервису
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
        var currentUserId = userInfo.GetProperty("sub").GetString();  // ← UUID
        var userEmail = userInfo.GetProperty("email").GetString();     // ← email (опционально)

        // 🔹 Проверка: сравниваем sub (UUID) с user_id из запроса
        if (string.IsNullOrEmpty(currentUserId) || currentUserId != user_id)
        {
            _logger.LogWarning("User {CurrentUserId} tried to access reports for {RequestedUserId}",
                currentUserId, user_id);
            // ✅ Исправлено: возвращаем 403 без имени схемы
            return StatusCode(403, new { error = "Доступ только к собственным отчётам" });
        }

        // Проверка актуальности данных
        /*var lastProcessed = await _clickHouse.GetLastProcessedDateAsync();
        if (lastProcessed == null)
        {
            // 🔹 Возвращаем информативный ответ, а не 500
            return StatusCode(503, new
            {
                error = "Отчёты ещё не сформированы. Дождитесь выполнения ETL-процесса.",
                retryAfter = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd HH:mm")
            });
        }

        if (to.HasValue && to.Value.Date > lastProcessed.Value.Date)
        {
            return BadRequest(new
            {
                error = "Запрошенный период ещё не обработан ETL",
                lastProcessedDate = lastProcessed.Value.Date.ToString("yyyy-MM-dd"),
                retryAfter = lastProcessed.Value.AddDays(1).ToString("yyyy-MM-dd HH:mm")
            });
        }*/

        var report = await _clickHouse.GetReportAsync(user_id, from, to);

        if (report.Count == 0)
            return NotFound(new { message = "Нет данных за указанный период" });

        return Ok(new
        {
            userId = user_id,
            period = new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") },
            rows = report
        });
    }
}