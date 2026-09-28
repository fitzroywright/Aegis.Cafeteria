namespace Aegis.Cafeteria.Infrastructure.Persistence;
using System.Text.Json;
using Aegis.Cafeteria.Domain.Accounts;

public static class PersistencePayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json)
    {
        if (typeof(T) == typeof(Stipend))
        {
            // Nullable payload fields also support events written before funding periods were stored.
            StipendPayload payload = JsonSerializer.Deserialize<StipendPayload>(json, Options)
                ?? throw new InvalidOperationException("Unable to deserialize Stipend.");
            Stipend stipend = new
            (
                  payload.Id
                , payload.CustomerId
                , payload.CashAmount
                , payload.CardReduction
                , payload.PaymentMode
                , payload.ProcessedByUserId
                , payload.ProcessedAt
                , payload.ExpenseReportReference
                , payload.Notes
                , payload.FundingYear
                , payload.FundingMonth
            );
            return (T)(object)stipend;
        }

        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name}.");
    }

    private sealed record StipendPayload
    (
          Guid Id
        , int CustomerId
        , decimal CashAmount
        , decimal CardReduction
        , StipendPaymentMode PaymentMode
        , string ProcessedByUserId
        , DateTimeOffset ProcessedAt
        , string? ExpenseReportReference
        , string? Notes
        , int? FundingYear
        , int? FundingMonth
    );
}
