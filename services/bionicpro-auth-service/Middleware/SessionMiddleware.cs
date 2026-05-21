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

    // SessionValidationMiddleware.cs
    public async Task InvokeAsync(HttpContext context)
    {
        var sessionService = context.RequestServices.GetRequiredService<IAuthSessionService>();
        var path = context.Request.Path.Value;

        // 🔍 Публичные пути (только те, что НЕ требуют сессии)
        if (path == "/auth/login" ||                    // ← POST логин
            path == "/auth/logout" ||                   // ← POST логаут (опционально)
            path?.StartsWith("/health") == true ||
            path?.StartsWith("/swagger") == true ||
            path?.StartsWith("/openapi") == true)
        {
            await _next(context);
            return;
        }

        // 🔍 Логирование входящих куки
        var allCookies = string.Join("; ", context.Request.Cookies.Select(c => $"{c.Key}={c.Value}"));
        Console.WriteLine($"[Middleware] Path: {path}, Cookies: [{allCookies}]");

        if (!context.Request.Cookies.TryGetValue(_cookieName, out var sessionId) ||
            string.IsNullOrEmpty(sessionId))
        {
            Console.WriteLine($"[Middleware] ❌ Cookie '{_cookieName}' NOT found. Available: [{string.Join(", ", context.Request.Cookies.Keys)}]");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Console.WriteLine($"[Middleware] ✅ Found session ID: {sessionId?.Substring(0, 20)}...");

        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null)
        {
            Console.WriteLine($"[Middleware] ❌ Session NOT found in cache for key: session:{sessionId}");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Console.WriteLine($"[Middleware] ✅ Session validated for userId: {session.UserId}");

        // Устанавливаем данные в контекст для контроллеров
        context.Items["UserId"] = session.UserId;
        context.Items["AccessToken"] = session.AccessToken;

        // 🔁 Ротация сессии (опционально, для защиты от fixation)
        var rotated = await sessionService.RotateSessionAsync(sessionId);
        if (rotated)
        {
            var newSession = await sessionService.GetSessionAsync(session.SessionId);
            if (newSession != null)
            {
                context.Response.Cookies.Append(_cookieName, newSession.SessionId, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = false,//app.Environment.IsProduction(), // ← отключить Secure для dev
                    SameSite = SameSiteMode.Strict,
                    Expires = newSession.ExpiresAt
                });
            }
        }

        await _next(context);
    }
}