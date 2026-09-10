namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Orders;

public sealed record LateRefundResult(OrderRefund Refund, PriorPeriodRefundCredit? PriorPeriodRefundCredit);
