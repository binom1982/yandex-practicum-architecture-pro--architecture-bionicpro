using bionicpro_reports.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ClickHouseService _clickHouse;

    public ReportsController(ClickHouseService clickHouse) => _clickHouse = clickHouse;

    [HttpGet("{userId}")]
    public async Task<IActionResult> GetReport(
        string userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var currentUserId = User.FindFirst("user_id")?.Value;
        if (string.IsNullOrEmpty(currentUserId) || currentUserId != userId)
            return Forbid("Доступ только к собственным отчётам");

        var lastProcessed = await _clickHouse.GetLastProcessedDateAsync();
        if (lastProcessed == null)
            return StatusCode(503, new { error = "ETL ещё не выполнялся" });

        if (to.HasValue && to.Value.Date > lastProcessed.Value.Date)
        {
            return BadRequest(new
            {
                error = "Запрошенный период ещё не обработан ETL",
                lastProcessedDate = lastProcessed.Value.Date.ToString("yyyy-MM-dd"),
                retryAfter = lastProcessed.Value.AddDays(1).ToString("yyyy-MM-dd HH:mm")
            });
        }

        var report = await _clickHouse.GetReportAsync(userId, from, to);

        if (report.Count == 0)
            return NotFound(new { message = "Нет данных за указанный период" });

        return Ok(new
        {
            userId,
            period = new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") },
            rows = report
        });
    }
}