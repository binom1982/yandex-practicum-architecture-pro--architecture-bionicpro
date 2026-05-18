using BionicproAuthService.Services;

namespace BionicproAuthService.Middleware;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _cookieName;

    // ❌ Не инжектим скоупированные сервисы в конструктор
    public SessionValidationMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _cookieName = config["Session:CookieName"] ?? "bionicpro_session";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ✅ Разрешаем сервис из скоупа текущего запроса
        var sessionService = context.RequestServices.GetRequiredService<IAuthSessionService>();

        var path = context.Request.Path.Value;

        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Cookies.TryGetValue(_cookieName, out var sessionId) ||
            string.IsNullOrEmpty(sessionId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var rotated = await sessionService.RotateSessionAsync(sessionId);
        if (rotated)
        {
            var newSession = await sessionService.GetSessionAsync(session.SessionId);
            if (newSession != null)
            {
                context.Response.Cookies.Append(_cookieName, newSession.SessionId, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = newSession.ExpiresAt
                });
            }
        }

        context.Items["UserId"] = session.UserId;
        context.Items["AccessToken"] = session.AccessToken;

        await _next(context);
    }

    private static bool IsPublicPath(string? path)
    {
        return path?.StartsWith("/auth") == true ||
               path?.StartsWith("/health") == true ||
               path?.StartsWith("/swagger") == true ||    // ← Swagger UI
               path?.StartsWith("/openapi") == true ||    // ← OpenAPI spec
               path?.Equals("/") == true;
    }
}