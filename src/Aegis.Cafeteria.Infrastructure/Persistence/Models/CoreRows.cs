namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class CustomerRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public byte[]? Photo { get; set; }
    public bool IsEligibleForCredit { get; set; } = true;
    public decimal AdditionalCreditLimit { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CustomerIdentifierRow
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public int Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class HolidayRow
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProductRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class OrderRow
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Status { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? AccountYear { get; set; }
    public int? AccountMonth { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
    public string? CashierUserId { get; set; }
    public string? TerminalId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItemRow> Items { get; set; } = [];
    public List<OrderPaymentRow> Payments { get; set; } = [];
    public List<OrderRefundRow> Refunds { get; set; } = [];
}

public sealed class OrderItemRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public sealed class OrderPaymentRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int Method { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class OrderRefundRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string RefundedBy { get; set; } = string.Empty;
    public DateTimeOffset RefundedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<OrderRefundItemRow> Items { get; set; } = [];
    public List<RefundAllocationRow> Allocations { get; set; } = [];
}

public sealed class OrderRefundItemRow
{
    public long Id { get; set; }
    public Guid RefundId { get; set; }
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public sealed class RefundAllocationRow
{
    public long Id { get; set; }
    public Guid RefundId { get; set; }
    public int Method { get; set; }
    public decimal Amount { get; set; }
}


public sealed class CafeteriaRatePolicyRow
{
    public int Id { get; set; }
    public string CreditLimitLunchItemCode { get; set; } = "FFP00";
    public decimal CreditLimitPrice { get; set; }
    public string SubsidyLunchItemCode { get; set; } = "FFP00";
    public decimal SubsidyCost { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
}
