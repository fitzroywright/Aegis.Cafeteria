namespace Aegis.Cafeteria.Services;

public sealed class ManagementApiOptions
{
    /// <summary>
    /// AD groups allowed to execute management financial operations. Group checks are performed
    /// against the authenticated Windows principal; client-supplied user names are never trusted.
    /// </summary>
    public string[] AuthorizedGroups { get; init; } = ["Cafeteria Managers", "Cafeteria Administrators", "Domain Admins"];

    /// <summary>
    /// Shared service-to-service API key for POS endpoints. Management endpoints use Windows/AD
    /// authentication instead of this key.
    /// </summary>
    public string? ApiKey { get; init; }
}

public sealed class MonthOpeningOptions
{
    public bool Enabled { get; init; } = true;
    public int RunHourLocal { get; init; } = 1;
}

public sealed class ScheduledEmailOptions
{
    public bool Enabled { get; init; }
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 25;
    public bool EnableSsl { get; init; }
    public string FromAddress { get; init; } = string.Empty;
    public string[] DailySummaryRecipients { get; init; } = [];
    public int DailySummaryHourLocal { get; init; } = 17;
    public DayOfWeek WeeklyEmployeeDay { get; init; } = DayOfWeek.Monday;
    public int WeeklyEmployeeHourLocal { get; init; } = 8;
    public string EmployeeEmailColumn { get; init; } = "Email";
    public bool PayrollNotificationEnabled { get; init; } = true;
    public string[] PayrollRecipients { get; init; } = [];
    public int PayrollReportHourLocal { get; init; } = 6;
}

public sealed class ServiceBackupOptions
{
    public bool Enabled { get; init; } = true;
    public string Directory { get; init; } = "backups";
    public int RunHourLocal { get; init; } = 2;
    public int RetentionDays { get; init; } = 30;
}


public sealed class ExternalPublicationOptions
{
    public int BatchSize { get; init; } = 100;
    public int RetryIntervalSeconds { get; init; } = 15;
    public int PoisonMessageAttemptLimit { get; init; } = 25;
}
