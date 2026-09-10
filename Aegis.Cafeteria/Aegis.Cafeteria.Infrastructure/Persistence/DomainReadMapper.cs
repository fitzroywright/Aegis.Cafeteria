namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;

public static class DomainReadMapper
{
    public static Customer ToDomain(CustomerRow x) => new(
        x.Id,
        x.Name,
        x.Photo,
        x.IsEligibleForCredit,
        x.AdditionalCreditLimit,
        x.IsActive,
        x.EmploymentStartDate);

    public static Order ToDomain(OrderRow x)
    {
        IDMethod? idMethod = x.IDMethod.HasValue ? (IDMethod?)x.IDMethod.Value : null;
        var order = new Order(x.Id, x.CustomerId, x.CreatedAt, idMethod, x.IdentificationCode);
        foreach (var item in x.Items.OrderBy(i => i.Id))
            order.AddItem(new OrderItem(item.Id, item.ProductId, item.ProductName, item.UnitPrice, item.Quantity));

        if (x.Status == (int)OrderStatus.Draft)
            return order;

        if (x.Status == (int)OrderStatus.Voided)
        {
            if (x.SubmittedAt.HasValue)
                order.Submit(x.SubmittedAt.Value);
            order.Void(x.VoidedBy ?? "system", x.VoidedAt ?? x.UpdatedAt, x.VoidReason ?? "Persisted void");
            return order;
        }

        if (!x.SubmittedAt.HasValue)
            throw new InvalidOperationException($"Persisted order {x.Id} is not draft but has no SubmittedAt value.");

        order.Submit(x.SubmittedAt.Value);
        if (x.Status == (int)OrderStatus.Submitted)
            return order;

        if (x.Status != (int)OrderStatus.Completed || !x.CompletedAt.HasValue)
            throw new InvalidOperationException($"Persisted order {x.Id} has an unsupported state.");

        var payments = x.Payments
            .OrderBy(p => p.RecordedAt)
            .Select(p => new OrderPayment(p.Id, (PaymentMethod)p.Method, p.Amount, p.RecordedAt))
            .ToArray();
        order.Complete(
            payments,
            x.CompletedAt.Value,
            x.CashierUserId ?? "legacy",
            x.TerminalId ?? "legacy",
            x.CashierName ?? x.CashierUserId ?? "legacy");

        foreach (var refundRow in x.Refunds.OrderBy(r => r.RefundedAt))
        {
            var items = refundRow.Items.Select(i => new OrderRefundItem(i.OrderItemId, i.Quantity, i.UnitPrice)).ToArray();
            var allocations = refundRow.Allocations.Select(a => new RefundAllocation((PaymentMethod)a.Method, a.Amount)).ToArray();
            order.RecordRefund(new OrderRefund(
                refundRow.Id,
                x.Id,
                items,
                allocations,
                refundRow.RefundedBy,
                refundRow.RefundedAt,
                refundRow.Reason));
        }

        return order;
    }
}
