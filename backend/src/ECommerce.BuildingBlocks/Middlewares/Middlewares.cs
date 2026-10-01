using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerce.BuildingBlocks.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace ECommerce.BuildingBlocks.Middlewares;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public const string CorrelationHeader = "X-Correlation-Id";

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault() ?? Guid.NewGuid().ToString();
        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Items[CorrelationHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        var statusCode = exception switch
        {
            ArgumentException => HttpStatusCode.BadRequest,
            InvalidOperationException => HttpStatusCode.Conflict,
            KeyNotFoundException => HttpStatusCode.NotFound,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.StatusCode = (int)statusCode;
        var correlationId = context.Items[CorrelationIdMiddleware.CorrelationHeader]?.ToString() ?? string.Empty;

        var problemDetails = new
        {
            type = $"https://api.ecommerce.com/errors/{(int)statusCode}",
            title = exception.GetType().Name,
            status = (int)statusCode,
            detail = exception.Message,
            instance = context.Request.Path.Value,
            correlationId,
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
    }
}

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private const string IdempotencyHeader = "Idempotency-Key";

    public IdempotencyMiddleware(RequestDelegate next, IServiceProvider serviceProvider)
    {
        _next = next;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Chỉ áp dụng cho POST và PATCH làm thay đổi state
        if (context.Request.Method != HttpMethods.Post && context.Request.Method != HttpMethods.Patch)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(IdempotencyHeader, out var keyValues) || string.IsNullOrWhiteSpace(keyValues))
        {
            // Không có Idempotency-Key thì tiếp tục thông thường
            await _next(context);
            return;
        }

        var key = keyValues.ToString();
        var endpoint = context.Request.Path.ToString();

        // Đọc body để tính request hash
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommonDbContext>();

        var existingRecord = await db.IdempotencyRecords.FirstOrDefaultAsync(x => x.Key == key);
        if (existingRecord != null)
        {
            if (existingRecord.Status == "COMPLETED" && existingRecord.ResponseCode.HasValue)
            {
                // Trả về kết quả đã cache từ trước (Tránh thực thi duplicate)
                context.Response.StatusCode = existingRecord.ResponseCode.Value;
                context.Response.ContentType = "application/json";
                context.Response.Headers[IdempotencyHeader] = key;
                context.Response.Headers["X-Cache-Lookup"] = "HIT-IDEMPOTENT";
                await context.Response.WriteAsync(existingRecord.ResponseBody ?? string.Empty);
                return;
            }

            if (existingRecord.Status == "PROCESSING")
            {
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Request is already in flight. Please wait." }));
                return;
            }
        }

        // Tạo record PROCESSING
        var record = new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            RequestHash = requestHash,
            Status = "PROCESSING",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        db.IdempotencyRecords.Add(record);
        await db.SaveChangesAsync();

        // Capture response
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await _next(context);

            responseBodyStream.Position = 0;
            var responseText = await new StreamReader(responseBodyStream).ReadToEndAsync();
            responseBodyStream.Position = 0;

            record.Status = "COMPLETED";
            record.ResponseCode = context.Response.StatusCode;
            record.ResponseBody = responseText;
            await db.SaveChangesAsync();

            await responseBodyStream.CopyToAsync(originalBodyStream);
        }
        catch
        {
            record.Status = "FAILED";
            await db.SaveChangesAsync();
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }
}
