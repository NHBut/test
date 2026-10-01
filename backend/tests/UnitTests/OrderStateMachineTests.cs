using ECommerce.Modules.Order.Domain;
using Xunit;

namespace ECommerce.UnitTests;

public class OrderStateMachineTests
{
    [Fact]
    public void Order_Creation_Should_Start_At_PendingPayment_With_Correct_Totals()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var order = Order.Create(customerId, "{}", shippingFee: 30000, discountAmount: 10000);

        // Act
        order.AddItem(Guid.NewGuid(), "iPhone 15", "Blue 128GB", "IP15-BLU-128", 20000000, 1);
        order.CompleteCreation();

        // Assert
        Assert.Equal(OrderStatus.PENDING_PAYMENT, order.Status);
        Assert.Equal(20000000, order.Subtotal);
        Assert.Equal(20020000, order.TotalAmount); // 20m - 10k + 30k = 20,020,000
        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderCreatedDomainEvent>(order.DomainEvents.First());
    }

    [Fact]
    public void Valid_Transitions_Should_Succeed()
    {
        // Arrange
        var order = Order.Create(Guid.NewGuid(), "{}");
        order.AddItem(Guid.NewGuid(), "AirPods Pro", "White", "APP-WHT", 5000000, 1);
        order.CompleteCreation();

        // Act & Assert
        // PENDING_PAYMENT -> PAID
        order.MarkAsPaid();
        Assert.Equal(OrderStatus.PAID, order.Status);

        // PAID -> CONFIRMED
        order.Confirm();
        Assert.Equal(OrderStatus.CONFIRMED, order.Status);

        // CONFIRMED -> PROCESSING
        order.StartProcessing();
        Assert.Equal(OrderStatus.PROCESSING, order.Status);

        // PROCESSING -> SHIPPING
        order.Ship();
        Assert.Equal(OrderStatus.SHIPPING, order.Status);

        // SHIPPING -> DELIVERED
        order.MarkAsDelivered();
        Assert.Equal(OrderStatus.DELIVERED, order.Status);

        Assert.Equal(6, order.StatusHistory.Count);
    }

    [Fact]
    public void Invalid_Transition_Should_Throw_InvalidOperationException()
    {
        // Arrange
        var order = Order.Create(Guid.NewGuid(), "{}");
        order.AddItem(Guid.NewGuid(), "MacBook Air", "Midnight", "MBA-MID", 25000000, 1);
        order.CompleteCreation();

        // Act & Assert: Không được phép nhảy từ PENDING_PAYMENT thẳng sang SHIPPING
        var ex = Assert.Throws<InvalidOperationException>(() => order.Ship());
        Assert.Contains("Invalid order state transition", ex.Message);
    }

    [Fact]
    public void Delivered_Order_Cannot_Be_Cancelled()
    {
        // Arrange
        var order = Order.Create(Guid.NewGuid(), "{}");
        order.AddItem(Guid.NewGuid(), "Mouse", "Black", "MS-BLK", 500000, 1);
        order.CompleteCreation();
        order.MarkAsPaid();
        order.Confirm();
        order.StartProcessing();
        order.Ship();
        order.MarkAsDelivered();

        // Act & Assert: Đã giao hàng thì không thể Cancel (phải qua Refund/Return)
        Assert.Throws<InvalidOperationException>(() => order.Cancel("Customer changed mind"));
    }
}
