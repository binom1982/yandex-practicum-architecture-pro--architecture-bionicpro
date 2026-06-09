using BionicproAuthService.Models;
using BionicproAuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BionicproAuthService.Middleware;

public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuthSessionOptions _options;

    public SessionValidationMiddleware(RequestDelegate next, IOptions<AuthSessionOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, IAuthSessionService sessionService)
    {
        // Пропускаем публичные эндпоинты
        if (context.Request.Path.StartsWithSegments("/auth/login") ||
            context.Request.Path.StartsWithSegments("/auth/callback") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        // Проверяем куку сессии
        if (context.Request.Cookies.TryGetValue(_options.CookieName, out var sessionId))
        {
            var session = await sessionService.GetSessionAsync(sessionId);

            if (session != null && !session.IsExpired)
            {
                // ✅ Сессия валидна — ротация + передача в контекст
                var rotated = await sessionService.RotateSessionAsync(session);
                var activeSession = rotated ?? session;

                // Обновляем куку если session_id изменился
                if (rotated != null && rotated.SessionId != sessionId)
                {
                    context.Response.Cookies.Append(
                        _options.CookieName,
                        rotated.SessionId,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = context.Request.IsHttps,
                            SameSite = SameSiteMode.Lax,
                            Expires = rotated.ExpiresAt,
                            Path = "/"
                        });
                }

                context.Items["UserId"] = activeSession.UserId;
                context.Items["Session"] = activeSession;

                await _next(context);
                return;
            }
        }

        // ❌ Сессия невалидна — очищаем куку и продолжаем (контроллер вернёт 401)
        if (context.Request.Cookies.ContainsKey(_options.CookieName))
            context.Response.Cookies.Delete(_options.CookieName);

        await _next(context);
    }
}