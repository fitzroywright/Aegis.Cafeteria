namespace Aegis.Cafeteria.Domain.Orders;

public sealed class Order
{
    private readonly List<OrderItem> items = [];
    private readonly List<OrderPayment> payments = [];
    private readonly List<OrderRefund> refunds = [];

    public Order(Guid id, int customerId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order ID is required.", nameof(id));
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId));

        Id = id;
        CustomerId = customerId;
        CreatedAt = createdAt;
        Status = OrderStatus.Draft;
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public DateTimeOffset CreatedAt { get; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }
    public string? CashierUserId { get; private set; }
    public string? TerminalId { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => this.items.AsReadOnly();
    public IReadOnlyCollection<OrderPayment> Payments => this.payments.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => this.refunds.AsReadOnly();

    public decimal Total => this.items.Sum(item => item.Total);
    public decimal AmountPaid => this.payments.Sum(payment => payment.Amount);
    public decimal CafeteriaAccountPaid => this.payments.Where(x => x.Method == PaymentMethod.CafeteriaAccount).Sum(x => x.Amount);
    public decimal CashPaid => this.payments.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount);
    public decimal RefundedAmount => this.refunds.Sum(x => x.Total);
    public decimal CafeteriaAccountRefunded => this.refunds.Sum(x => x.CafeteriaAccountAmount);
    public decimal CashRefunded => this.refunds.Sum(x => x.CashAmount);
    public decimal NetCafeteriaAccountCharge => CafeteriaAccountPaid - CafeteriaAccountRefunded;
    public decimal NetCashCollected => CashPaid - CashRefunded;
    public decimal NetTotal => Total - RefundedAmount;
    public RefundStatus RefundStatus => RefundedAmount == 0m
        ? RefundStatus.None
        : RefundedAmount == Total ? RefundStatus.Full : RefundStatus.Partial;

    public void AddItem(OrderItem item)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(item);
        this.items.Add(item);
    }

    public void RemoveItem(Guid orderItemId)
    {
        EnsureDraft();
        var item = this.items.SingleOrDefault(x => x.Id == orderItemId)
            ?? throw new InvalidOperationException("The order item was not found.");
        this.items.Remove(item);
    }

    public void Submit(DateTimeOffset submittedAt)
    {
        EnsureDraft();
        if (this.items.Count == 0)
            throw new InvalidOperationException("An empty order cannot be submitted.");

        Status = OrderStatus.Submitted;
        SubmittedAt = submittedAt;
    }

    public void Complete(IEnumerable<OrderPayment> payments, DateTimeOffset completedAt)
        => Complete(payments, completedAt, "system", "unknown");

    public void Complete(IEnumerable<OrderPayment> payments, DateTimeOffset completedAt, string cashierUserId, string terminalId)
    {
        if (Status != OrderStatus.Submitted)
            throw new InvalidOperationException("Only a submitted order can be completed.");
        ArgumentNullException.ThrowIfNull(payments);

        var paymentArray = payments.ToArray();
        if (paymentArray.Length == 0)
            throw new InvalidOperationException("A completed order must have payment.");
        if (paymentArray.Sum(x => x.Amount) != Total)
            throw new InvalidOperationException("Payments must equal the order total before completion.");

        if (string.IsNullOrWhiteSpace(cashierUserId))
            throw new ArgumentException("Cashier user ID is required.", nameof(cashierUserId));
        if (string.IsNullOrWhiteSpace(terminalId))
            throw new ArgumentException("Terminal ID is required.", nameof(terminalId));

        this.payments.AddRange(paymentArray);
        Status = OrderStatus.Completed;
        CompletedAt = completedAt;
        CashierUserId = cashierUserId.Trim();
        TerminalId = terminalId.Trim();
    }

    public void Void(string voidedBy, DateTimeOffset voidedAt, string reason)
    {
        if (Status is OrderStatus.Completed or OrderStatus.Voided)
            throw new InvalidOperationException("A completed or already voided order cannot be voided.");
        if (string.IsNullOrWhiteSpace(voidedBy))
            throw new ArgumentException("Voiding user is required.", nameof(voidedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A void reason is required.", nameof(reason));

        Status = OrderStatus.Voided;
        VoidedBy = voidedBy.Trim();
        VoidedAt = voidedAt;
        VoidReason = reason.Trim();
    }

    public int GetRefundedQuantity(Guid orderItemId)
        => this.refunds.SelectMany(x => x.Items)
            .Where(x => x.OrderItemId == orderItemId)
            .Sum(x => x.Quantity);

    public void RecordRefund(OrderRefund refund)
    {
        if (Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only a completed order can be refunded.");
        ArgumentNullException.ThrowIfNull(refund);
        if (refund.OrderId != Id)
            throw new InvalidOperationException("The refund does not belong to this order.");
        if (RefundedAmount + refund.Total > Total)
            throw new InvalidOperationException("Refunds cannot exceed the original order total.");

        foreach (var refundItem in refund.Items)
        {
            var originalItem = this.items.SingleOrDefault(x => x.Id == refundItem.OrderItemId)
                ?? throw new InvalidOperationException("A refunded item does not exist on the original order.");
            if (refundItem.UnitPrice != originalItem.UnitPrice)
                throw new InvalidOperationException("Refund item price must match the original order item price.");

            var remainingQuantity = originalItem.Quantity - GetRefundedQuantity(originalItem.Id);
            if (refundItem.Quantity > remainingQuantity)
                throw new InvalidOperationException("Refund quantity exceeds the remaining refundable quantity.");
        }

        var remainingAccountTender = CafeteriaAccountPaid - CafeteriaAccountRefunded;
        var remainingCashTender = CashPaid - CashRefunded;
        if (refund.CafeteriaAccountAmount > remainingAccountTender)
            throw new InvalidOperationException("Cafeteria-account refund exceeds the remaining cafeteria-account tender.");
        if (refund.CashAmount > remainingCashTender)
            throw new InvalidOperationException("Cash refund exceeds the remaining cash tender.");

        this.refunds.Add(refund);
    }

    private void EnsureDraft()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Only a draft order can be modified.");
    }
}
