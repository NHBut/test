using System.Text.Json;
using ECommerce.BuildingBlocks.CQRS;
using ECommerce.BuildingBlocks.Database;
using ECommerce.BuildingBlocks.Outbox;
using ECommerce.Modules.Payment.Domain;
using ECommerce.Modules.Payment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerce.Modules.Payment.Application;

public record InitiatePaymentCommand(Guid OrderId, decimal Amount, string PaymentMethod, string ReturnUrl) : ICommand<string>;

public record WebhookCommand(string Gateway, string TransactionCode, decimal Amount, string Status, string Signature, string RawData) : ICommand<bool>;

public record RefundPaymentCommand(Guid PaymentId, decimal Amount, string Reason) : ICommand<bool>;

public class PaymentHandlers :
    ICommandHandler<InitiatePaymentCommand, string>,
    ICommandHandler<WebhookCommand, bool>,
    ICommandHandler<RefundPaymentCommand, bool>
{
    private readonly PaymentDbContext _payDb;
    private readonly CommonDbContext _commonDb;
    private readonly IOutboxService _outbox;
    private readonly IPaymentGatewayService _gateway;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentHandlers> _logger;

    public PaymentHandlers(
        PaymentDbContext payDb,
        CommonDbContext commonDb,
        IOutboxService outbox,
        IPaymentGatewayService gateway,
        IConfiguration config,
        ILogger<PaymentHandlers> logger)
    {
        _payDb = payDb;
        _commonDb = commonDb;
        _outbox = outbox;
        _gateway = gateway;
        _config = config;
        _logger = logger;
    }

    public async Task<string> Handle(InitiatePaymentCommand request, CancellationToken cancellationToken)
    {
        var existing = await _payDb.Payments.FirstOrDefaultAsync(x => x.OrderId == request.OrderId, cancellationToken);
        if (existing == null)
        {
            existing = Domain.Payment.Create(request.OrderId, request.Amount, request.PaymentMethod);
            _payDb.Payments.Add(existing);
            await _payDb.SaveChangesAsync(cancellationToken);
        }

        return _gateway.CreatePaymentUrl(existing.Id, existing.OrderId, existing.Amount, request.ReturnUrl);
    }

    public async Task<bool> Handle(WebhookCommand request, CancellationToken cancellationToken)
    {
        var secret = _config["Payment:GatewaySecret"] ?? "MOCK_PAYMENT_SECRET_KEY_123456789";

        // 1. Xác thực HMAC Signature
        if (!_gateway.VerifySignature(request.RawData, request.Signature, secret))
        {
            _logger.LogWarning("Invalid webhook signature from gateway {Gateway}", request.Gateway);
            throw new UnauthorizedAccessException("Invalid webhook signature.");
        }

        // 2. Chống Duplicate Webhook (Idempotency)
        var duplicateTx = await _payDb.PaymentTransactions
            .AnyAsync(t => t.Gateway == request.Gateway && t.TransactionCode == request.TransactionCode, cancellationToken);

        if (duplicateTx)
        {
            _logger.LogInformation("Duplicate webhook detected for Gateway {Gateway}, Tx {Tx}. Skipping.", request.Gateway, request.TransactionCode);
            return true; // Trả về success để Gateway không tiếp tục retry
        }

        // 3. Tìm Payment record
        // Trong thực tế parse orderId/paymentId từ rawData
        var payment = await _payDb.Payments
            .Include(x => x.Transactions)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment == null) return false;

        var json = JsonSerializer.Serialize(request);
        if (request.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            payment.RecordSuccess(request.Gateway, request.TransactionCode, json);
        }
        else
        {
            payment.RecordFailure(request.Gateway, request.TransactionCode, "Gateway reported failure", json);
        }

        _outbox.EnqueueDomainEvent(payment, _commonDb);

        await _payDb.SaveChangesAsync(cancellationToken);
        await _commonDb.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _payDb.Payments
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(x => x.Id == request.PaymentId, cancellationToken);

        if (payment == null) return false;

        var refundTxCode = $"REF-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";
        payment.RecordRefund("INTERNAL", refundTxCode, request.Amount, JsonSerializer.Serialize(new { request.Reason }));

        _outbox.EnqueueDomainEvent(payment, _commonDb);

        await _payDb.SaveChangesAsync(cancellationToken);
        await _commonDb.SaveChangesAsync(cancellationToken);

        return true;
    }
}
