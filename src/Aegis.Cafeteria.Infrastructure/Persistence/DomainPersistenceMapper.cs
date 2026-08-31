namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;

public static class DomainPersistenceMapper
{
    public static CustomerRow ToRow(Customer x, long revision, DateTimeOffset updatedAt) => new()
    {
        Id = x.Id,
        Name = x.Name,
        NormalizedName = NormalizeSearchText(x.Name),
        Photo = x.Photo,
        IsEligibleForCredit = x.IsEligibleForCredit,
        AdditionalCreditLimit = x.AdditionalCreditLimit,
        Revision = revision,
        UpdatedAt = updatedAt
    };

    public static CustomerIdentifierRow ToRow(CustomerIdentifier x, long revision, DateTimeOffset updatedAt) => new()
    {
        Id = x.Id,
        CustomerId = x.CustomerId,
        Type = (int)x.Type,
        Value = x.Value,
        NormalizedValue = NormalizeIdentifier(x.Type, x.Value),
        Revision = revision,
        UpdatedAt = updatedAt
    };

    public static MonthlyCreditAllocationRow ToRow(MonthlyCreditAllocation x, long revision, DateTimeOffset updatedAt) => new()
    {
        CustomerId = x.CustomerId,
        Year = x.Year,
        Month = x.Month,
        EligibleWorkingDays = x.EligibleWorkingDays,
        IsEligibleForCredit = x.IsEligibleForCredit,
        CreditLimitPrice = x.CreditLimitPrice,
        SubsidyCost = x.SubsidyCost,
        EmployeeFundedAllocation = x.EmployeeFundedAllocation,
        SubsidyAllocation = x.SubsidyAllocation,
        TotalCreditLimit = x.TotalCreditLimit,
        CalculatedAt = x.CalculatedAt,
        Revision = revision,
        UpdatedAt = updatedAt
    };

    public static ProductRow ToRow(Product x, long revision, DateTimeOffset updatedAt) => new()
    { Id = x.Id, Name = x.Name, Price = x.Price, Revision = revision, UpdatedAt = updatedAt };

    public static OrderRow ToRow(Order x, long revision, DateTimeOffset updatedAt) => new()
    {
        Id=x.Id, CustomerId=x.CustomerId, CreatedAt=x.CreatedAt, Status=(int)x.Status, SubmittedAt=x.SubmittedAt,
        CompletedAt=x.CompletedAt, AccountYear=(x.CompletedAt ?? x.VoidedAt)?.Year, AccountMonth=(x.CompletedAt ?? x.VoidedAt)?.Month, VoidedAt=x.VoidedAt, VoidedBy=x.VoidedBy, VoidReason=x.VoidReason, CashierUserId=x.CashierUserId, TerminalId=x.TerminalId,
        Revision=revision, UpdatedAt=updatedAt,
        Items=x.Items.Select(i=>new OrderItemRow{Id=i.Id,OrderId=x.Id,ProductId=i.ProductId,ProductName=i.ProductName,UnitPrice=i.UnitPrice,Quantity=i.Quantity}).ToList(),
        Payments=x.Payments.Select(p=>new OrderPaymentRow{Id=p.Id,OrderId=x.Id,Method=(int)p.Method,Amount=p.Amount,RecordedAt=p.RecordedAt}).ToList(),
        Refunds=x.Refunds.Select(r=>new OrderRefundRow
        {
            Id=r.Id,OrderId=x.Id,RefundedBy=r.RefundedBy,RefundedAt=r.RefundedAt,Reason=r.Reason,
            Items=r.Items.Select(i=>new OrderRefundItemRow{RefundId=r.Id,OrderItemId=i.OrderItemId,Quantity=i.Quantity,UnitPrice=i.UnitPrice}).ToList(),
            Allocations=r.Allocations.Select(a=>new RefundAllocationRow{RefundId=r.Id,Method=(int)a.Method,Amount=a.Amount}).ToList()
        }).ToList()
    };

    public static string NormalizeSearchText(string value) => value.Trim().ToUpperInvariant();

    public static string NormalizeIdentifier(CustomerIdentifierType type, string value)
    {
        var trimmed = value.Trim();
        return type == CustomerIdentifierType.Trn
            ? new string(trimmed.Where(char.IsDigit).ToArray())
            : trimmed.ToUpperInvariant();
    }
}
