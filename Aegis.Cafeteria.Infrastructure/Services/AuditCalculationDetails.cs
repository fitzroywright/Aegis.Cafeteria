namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using System.Globalization;
using System.Text;
using System.Text.Json;

public static class AuditCalculationDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Format(AuditTrailSnapshot? audit)
    {
        if (audit is null)
            return "Select an audit record to view its details.";

        StringBuilder text = new();
        text.AppendLine($"Audit #{audit.Id} | {audit.Action} | Employee ID: {audit.CustomerId}");
        text.AppendLine($"Recorded: {audit.OccurredAt:yyyy-MM-dd HH:mm:ss zzz} | Actor: {audit.Actor}");
        text.AppendLine($"Result: {(audit.Succeeded ? "Succeeded" : "Failed")}");
        if (audit.OperationId.HasValue) text.AppendLine($"Operation: {audit.OperationId}");
        if (!string.IsNullOrWhiteSpace(audit.Detail)) text.AppendLine(audit.Detail);

        try
        {
            if (audit.Action.Equals("MonthlyAllocation", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(audit.AfterJson))
                    text.AppendLine("Calculation payload is unavailable for this audit record.");
                else
                    AppendAllocation(text, Read<MonthlyAllocationServiceEvent>(audit.AfterJson), "MONTHLY ALLOCATION");
            }
            else if (audit.Action.Equals("MonthlyAllocationRecalculation", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(audit.AfterJson))
                    text.AppendLine("Calculation payload is unavailable for this audit record.");
                else
                {
                    var change = Read<MonthlyAllocationRecalculationServiceEvent>(audit.AfterJson);
                    text.AppendLine($"Recalculated by: {change.RecalculatedBy} at {change.RecalculatedAt:yyyy-MM-dd HH:mm:ss zzz}");
                    MonthlyCreditAllocation before = AppendAllocation(text, change.PreviousAllocation, "PREVIOUS ALLOCATION");
                    MonthlyCreditAllocation after = AppendAllocation(text, change.ReplacementAllocation, "REPLACEMENT ALLOCATION");
                    text.AppendLine($"Credit change: {Money(after.TotalCreditLimit - before.TotalCreditLimit)}");
                    text.AppendLine($"Subsidy change: {Money(after.SubsidyAllocation - before.SubsidyAllocation)}");
                }
            }
            else
            {
                AppendPayload(text, "Before", audit.BeforeJson);
                AppendPayload(text, "After", audit.AfterJson);
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException)
        {
            text.AppendLine("The stored calculation payload could not be interpreted.");
            AppendPayload(text, "Stored payload", audit.AfterJson);
        }

        return text.ToString();
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new JsonException("Calculation payload was empty.");

    private static MonthlyCreditAllocation AppendAllocation(StringBuilder text, MonthlyAllocationServiceEvent? value, string title)
    {
        if (value is null) throw new JsonException("Allocation is missing.");
        MonthlyCreditAllocation allocation = new(value.CustomerId, value.Year, value.Month,
            value.EligibleWorkingDays, value.IsEligibleForCredit, value.CreditLimitPrice, value.SubsidyCost, value.CalculatedAt);

        text.AppendLine();
        text.AppendLine(title);
        text.AppendLine($"Employee: {value.EmployeeName} (ID {value.CustomerId}) | Month: {value.Year:D4}-{value.Month:D2}");
        text.AppendLine($"Eligible for credit: {(value.IsEligibleForCredit ? "Yes" : "No")} | Eligible working days: {value.EligibleWorkingDays}");
        text.AppendLine($"Daily credit rate: {Money(value.CreditLimitPrice)} | Daily subsidy rate: {Money(value.SubsidyCost)}");
        if (value.IsEligibleForCredit)
        {
            text.AppendLine($"Monthly credit: {value.EligibleWorkingDays} × {Money(value.CreditLimitPrice)} = {Money(allocation.TotalCreditLimit)}");
            text.AppendLine($"Company subsidy: min({Money(allocation.TotalCreditLimit)}, {value.EligibleWorkingDays} × {Money(value.SubsidyCost)}) = {Money(allocation.SubsidyAllocation)}");
        }
        else
        {
            text.AppendLine("Monthly credit: J$0.00 (employee was not eligible)");
            text.AppendLine("Company subsidy: J$0.00");
        }
        text.AppendLine($"Employee-funded portion: {Money(allocation.TotalCreditLimit)} − {Money(allocation.SubsidyAllocation)} = {Money(allocation.EmployeeFundedAllocation)}");
        text.AppendLine($"Calculated: {value.CalculatedAt:yyyy-MM-dd HH:mm:ss zzz} | Reason: {value.AllocationReason}");
        text.AppendLine("These are the recorded monthly entitlements. Current Credit Available also reflects account activity.");
        return allocation;
    }

    private static string Money(decimal value) => "J$" + value.ToString("#,##0.00####", CultureInfo.InvariantCulture);

    private static void AppendPayload(StringBuilder text, string title, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        text.AppendLine();
        text.AppendLine(title + ":");
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            text.AppendLine(JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            text.AppendLine(json);
        }
    }
}
