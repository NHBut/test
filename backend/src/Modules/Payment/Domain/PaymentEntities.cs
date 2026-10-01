using ECommerce.BuildingBlocks.Domain;

namespace ECommerce.Modules.Payment.Domain;

public enum PaymentStatus
{
    PENDING,
    AUTHORIZED,
    CAPTURED,
    FAILED,
    REFUNDED
}

public enum TransactionType
{
    CHARGE,
    REFUND,
    VOID
}

public record PaymentCompletedDomainEvent(Guid PaymentId, Guid OrderId, decimal Amount, string GatewayTxId) : IDomainEvent;
public record PaymentFailedDomainEvent(Guid PaymentId, Guid OrderId, string Reason) : IDomainEvent;
public record PaymentRefundedDomainEvent(Guid PaymentId, Guid OrderId, decimal RefundedAmount) : IDomainEvent;

public class Payment : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string PaymentMethod { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; } = PaymentStatus.PENDING;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private readonly List<PaymentTransaction> _transactions = new();
    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions.AsReadOnly();

    private Payment() { }

    public static Payment Create(Guid orderId, decimal amount, string paymentMethod, string currency = "VND")
    {
        if (amount <= 0) throw new ArgumentException("Payment amount must be greater than zero.");
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Currency = currency,
            Status = PaymentStatus.PENDING,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void RecordSuccess(string gateway, string gatewayTxCode, string rawResponseJson)
    {
        if (Status == PaymentStatus.CAPTURED) return; // Idempotent guard

        Status = PaymentStatus.CAPTURED;
        UpdatedAt = DateTime.UtcNow;

        _transactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PaymentId = Id,
            Gateway = gateway,
            TransactionCode = gatewayTxCode,
            Type = TransactionType.CHARGE,
            Amount = Amount,
            Status = "SUCCESS",
            RawResponseJson = rawResponseJson,
            CreatedAt = DateTime.UtcNow
        });

        AddDomainEvent(new PaymentCompletedDomainEvent(Id, OrderId, Amount, gatewayTxCode));
    }

    public void RecordFailure(string gateway, string gatewayTxCode, string reason, string rawResponseJson)
    {
        if (Status == PaymentStatus.CAPTURED) return; // Không ghi đè nếu đã thanh toán thành công (Out-of-order guard)

        Status = PaymentStatus.FAILED;
        UpdatedAt = DateTime.UtcNow;

        _transactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PaymentId = Id,
            Gateway = gateway,
            TransactionCode = gatewayTxCode,
            Type = TransactionType.CHARGE,
            Amount = Amount,
            Status = "FAILED",
            RawResponseJson = rawResponseJson,
            CreatedAt = DateTime.UtcNow
        });

        AddDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, reason));
    }

    public void RecordRefund(string gateway, string refundTxCode, decimal refundAmount, string rawResponseJson)
    {
        if (Status != PaymentStatus.CAPTURED)
            throw new InvalidOperationException("Can only refund captured payments.");

        var totalRefunded = _transactions.Where(t => t.Type == TransactionType.REFUND).Sum(t => t.Amount);
        if (totalRefunded + refundAmount > Amount)
            throw new InvalidOperationException("Refund amount exceeds original captured amount.");

        _transactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PaymentId = Id,
            Gateway = gateway,
            TransactionCode = refundTxCode,
            Type = TransactionType.REFUND,
            Amount = refundAmount,
            Status = "REFUNDED",
            RawResponseJson = rawResponseJson,
            CreatedAt = DateTime.UtcNow
        });

        if (totalRefunded + refundAmount == Amount)
        {
            Status = PaymentStatus.REFUNDED;
        }

        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new PaymentRefundedDomainEvent(Id, OrderId, refundAmount));
    }
}

public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string TransactionCode { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
