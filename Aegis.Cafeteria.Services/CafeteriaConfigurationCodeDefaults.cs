using System.Globalization;
using Aegis.Cafeteria.Infrastructure.Configuration;

namespace Aegis.Cafeteria.Services;

internal static class CafeteriaConfigurationCodeDefaults
{
    private static readonly FfpIntegrationOptions Ffp = new();
    private static readonly ManagementApiOptions Management = new();
    private static readonly MonthOpeningOptions MonthOpening = new();
    private static readonly ScheduledEmailOptions Email = new();
    private static readonly ExternalPublicationOptions ExternalPublication = new();
    private static readonly CafeteriaLoggingOptions Logging = new();

    public static string? Get(string key) => key switch
    {
        "ServicePersistence:ConnectionString" => "Data Source=cafeteria.services.db",

        "FfpIntegration:Enabled" => Bool(Ffp.Enabled),
        "FfpIntegration:CreditLimitLunchItemCode" => Text(Ffp.CreditLimitLunchItemCode),
        "FfpIntegration:SubsidyLunchItemCode" => Text(Ffp.SubsidyLunchItemCode),
        "FfpIntegration:MonthlyAllocationScanType" => Number(Ffp.MonthlyAllocationScanType),
        "FfpIntegration:PurchaseScanType" => Number(Ffp.PurchaseScanType),
        "FfpIntegration:StipendScanType" => Number(Ffp.StipendScanType),
        "FfpIntegration:RefundScanType" => Number(Ffp.RefundScanType),
        "FfpIntegration:AdjustmentScanType" => Number(Ffp.AdjustmentScanType),
        "FfpIntegration:Encrypt" => "true", // BuildTrustedFfpOptions uses GetValue("Encrypt", true).
        "FfpIntegration:TrustServerCertificate" => "false", // BuildTrustedFfpOptions uses GetValue(..., false).

        "ManagementApi:AuthorizedGroups" => List(Management.AuthorizedGroups),

        "ExternalPublication:BatchSize" => Number(ExternalPublication.BatchSize),
        "ExternalPublication:RetryIntervalSeconds" => Number(ExternalPublication.RetryIntervalSeconds),
        "ExternalPublication:PoisonMessageAttemptLimit" => Number(ExternalPublication.PoisonMessageAttemptLimit),

        "MonthOpening:Enabled" => Bool(MonthOpening.Enabled),
        "MonthOpening:RunHourLocal" => Number(MonthOpening.RunHourLocal),

        "ScheduledEmail:Enabled" => Bool(Email.Enabled),
        "ScheduledEmail:SmtpHost" => Text(Email.SmtpHost),
        "ScheduledEmail:SmtpPort" => Number(Email.SmtpPort),
        "ScheduledEmail:EnableSsl" => Bool(Email.EnableSsl),
        "ScheduledEmail:FromAddress" => Text(Email.FromAddress),
        "ScheduledEmail:DailySummaryRecipients" => List(Email.DailySummaryRecipients),
        "ScheduledEmail:PayrollRecipients" => List(Email.PayrollRecipients),

        "Logging:ApplicationName" => Text(Logging.ApplicationName),
        "Logging:GraylogEnabled" => Bool(Logging.GraylogEnabled),
        "Logging:GraylogHost" => Text(Logging.GraylogHost),
        "Logging:GraylogPort" => Number(Logging.GraylogPort),

        _ => null
    };

    private static string Bool(bool value) => value ? "true" : "false";
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? List(IEnumerable<string> values)
    {
        string[] items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        return items.Length == 0 ? null : string.Join(", ", items);
    }
}
