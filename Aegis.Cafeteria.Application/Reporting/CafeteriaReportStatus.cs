namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Application.Abstractions;

public enum CafeteriaReportState
{
    Complete = 0,
    DataIncomplete = 1
}

public sealed record CafeteriaReportStatus(
    CafeteriaReportState State,
    DateTimeOffset? LastSuccessfulSync,
    int PendingSyncItems,
    int FailedSyncItems,
    string? LastSyncError)
{
    public string DisplayStatus => State == CafeteriaReportState.Complete
        ? "Complete"
        : "Data Incomplete";

    public static CafeteriaReportStatus FromPublicationStatus(ExternalPublicationStatus publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        int pending = Math.Max(0, publication.PendingOperations);
        int failed = Math.Max(0, publication.PoisonedOperations);
        bool incomplete = pending > 0 || failed > 0;

        return new CafeteriaReportStatus(
            incomplete ? CafeteriaReportState.DataIncomplete : CafeteriaReportState.Complete,
            publication.LastSuccessfulPublication,
            pending,
            failed,
            string.IsNullOrWhiteSpace(publication.LastError) ? null : publication.LastError.Trim());
    }

    public string ToReportHeader()
    {
        string lastSync = LastSuccessfulSync.HasValue
            ? LastSuccessfulSync.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
            : "Not Available";

        List<string> lines =
        [
            $"Report Status: {DisplayStatus}",
            $"Last Successful Sync: {lastSync}"
        ];

        if (State == CafeteriaReportState.DataIncomplete)
        {
            lines.Add($"Pending Sync Items: {PendingSyncItems}");
            lines.Add($"Failed Sync Items: {FailedSyncItems}");

            if (!string.IsNullOrWhiteSpace(LastSyncError))
            {
                lines.Add($"Last Sync Error: {LastSyncError}");
            }

            lines.Add("This report was generated using the data available at the time shown above. Transactions that have not synchronized may not yet be included.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
