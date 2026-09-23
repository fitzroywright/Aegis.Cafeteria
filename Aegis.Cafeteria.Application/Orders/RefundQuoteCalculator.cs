namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Domain.Orders;

public sealed record RefundQuote
(
      decimal RefundTotal
    , decimal RemainingCafeteriaAccount
    , decimal RemainingCredit
    , decimal RemainingCash
    , decimal MinimumCafeteriaAccount
    , decimal MaximumCafeteriaAccount
    , decimal CafeteriaAccountAmount
    , decimal CreditAmount
    , decimal CashAmount
    , bool IsValid
    , string? ValidationMessage
);

public static class RefundQuoteCalculator
{
    public static RefundQuote Calculate
    (
          Order order
        , IEnumerable<(Guid OrderItemId, int Quantity)> selections
        , decimal? requestedCafeteriaAccountAmount = null
    )
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(selections);

        (Guid OrderItemId, int Quantity)[] selected = selections.ToArray();

        if (selected.Length == 0)
        {
            return Empty(order, "Select at least one item quantity to refund.");
        }

        decimal refundTotal = 0m;
        foreach ((Guid orderItemId, int quantity) in selected)
        {
            OrderItem? item = order.Items.SingleOrDefault(candidate => candidate.Id == orderItemId);
            if (item is null)
            {
                return Empty(order, "A selected refund item does not exist on the order.");
            }

            int remainingQuantity = item.Quantity - order.GetRefundedQuantity(item.Id);
            if (quantity <= 0 || quantity > remainingQuantity)
            {
                return Empty(order, "A refund quantity exceeds the remaining refundable quantity.");
            }

            refundTotal += item.UnitPrice * quantity;
        }

        decimal remainingAccount = Math.Max(0m, order.CafeteriaAccountPaid - order.CafeteriaAccountRefunded);
        decimal remainingCredit = Math.Max(0m, order.CreditPaid - order.CreditRefunded);
        decimal remainingCash = Math.Max(0m, order.CashPaid - order.CashRefunded);

        decimal accountMinimum = Math.Max(0m, refundTotal - remainingCredit - remainingCash);
        decimal accountMaximum = Math.Min(refundTotal, remainingAccount);
        decimal accountAmount = requestedCafeteriaAccountAmount ?? Math.Min(refundTotal, remainingAccount);
        accountAmount = Math.Clamp(accountAmount, accountMinimum, accountMaximum);

        decimal afterAccount = Math.Max(0m, refundTotal - accountAmount);
        decimal creditAmount = Math.Min(afterAccount, remainingCredit);
        decimal cashAmount = Math.Max(0m, afterAccount - creditAmount);

        bool valid =
            accountAmount <= remainingAccount
            && creditAmount <= remainingCredit
            && cashAmount <= remainingCash
            && accountAmount + creditAmount + cashAmount == refundTotal;

        return new RefundQuote
        (
              refundTotal
            , remainingAccount
            , remainingCredit
            , remainingCash
            , accountMinimum
            , accountMaximum
            , accountAmount
            , creditAmount
            , cashAmount
            , valid
            , valid ? null : "Refund allocation must match the selected total and cannot exceed the original remaining Employee Card, Credit, or Cash tender."
        );
    }

    private static RefundQuote Empty(Order order, string message)
    {
        return new RefundQuote
        (
              0m
            , Math.Max(0m, order.CafeteriaAccountPaid - order.CafeteriaAccountRefunded)
            , Math.Max(0m, order.CreditPaid - order.CreditRefunded)
            , Math.Max(0m, order.CashPaid - order.CashRefunded)
            , 0m
            , 0m
            , 0m
            , 0m
            , 0m
            , false
            , message
        );
    }
}
