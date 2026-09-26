namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Orders;
using System.Text.Json;

public static class PosTransactionEventFactory
{
    public static PosTransactionEvent Create(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        OrderRefund? latestRefund = order.Refunds.OrderByDescending(refund => refund.RefundedAt).FirstOrDefault();
        string kind;
        string actor;
        DateTimeOffset occurredAt;
        string? reason;
        Guid eventId;

        if (order.Status == OrderStatus.Voided)
        {
            kind = "SaleVoided";
            actor = order.VoidedBy ?? "SYSTEM";
            occurredAt = order.VoidedAt ?? DateTimeOffset.UtcNow;
            reason = order.VoidReason;
            eventId = DeterministicEventId(order.Id, kind, occurredAt, null);
        }
        else if (latestRefund is not null)
        {
            kind = order.RefundStatus == RefundStatus.Full ? "RefundIssuedFull" : "RefundIssuedPartial";
            actor = latestRefund.RefundedBy;
            occurredAt = latestRefund.RefundedAt;
            reason = latestRefund.Reason;
            eventId = latestRefund.Id;
        }
        else
        {
            kind = order.CreditPaid > 0m ? "CreditSaleCompleted" : "SaleCompleted";
            actor = order.CashierUserId ?? "SYSTEM";
            occurredAt = order.CompletedAt ?? order.CreatedAt;
            reason = null;
            eventId = order.Id;
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            order.Id,
            order.CustomerId,
            Status = order.Status.ToString(),
            order.Total,
            order.CafeteriaAccountPaid,
            order.CashPaid,
            order.CreditPaid,
            order.RefundedAmount,
            order.CafeteriaAccountRefunded,
            order.CashRefunded,
            order.CreditRefunded,
            order.NetTotal,
            IdentificationMethod = order.IdentificationMethod?.ToString(),
            IdentificationCode = order.IdentificationCode,
            Items = order.Items.Select(item => new { item.Id, item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.Total }).ToArray(),
            Payments = order.Payments.Select(payment => new { payment.Id, Method = payment.Method.ToString(), payment.Amount, payment.RecordedAt }).ToArray(),
            Refunds = order.Refunds.Select(refund => new { refund.Id, refund.Total, refund.CafeteriaAccountAmount, refund.CashAmount, refund.CreditAmount, refund.RefundedBy, refund.RefundedAt, refund.Reason }).ToArray()
        });

        return new PosTransactionEvent(eventId, order.Id, kind, order.Id, order.CustomerId, actor, order.TerminalId, occurredAt.Year, occurredAt.Month, occurredAt, payloadJson, reason);
    }

    private static Guid DeterministicEventId(Guid orderId, string kind, DateTimeOffset occurredAt, Guid? childId)
    {
        string key = $"{orderId:D}|{kind}|{occurredAt:O}|{childId?.ToString("D") ?? string.Empty}";
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
