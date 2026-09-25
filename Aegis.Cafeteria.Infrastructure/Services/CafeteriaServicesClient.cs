namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Services;
using System.Net.Http.Json;

public sealed record CustomerNotificationPreferenceSnapshot
(
      int CustomerId
    , bool PreferenceAsked
    , bool WeeklySummaryEnabled
    , string? EmailAddress
);

public sealed record MonthOperationResult
(
      int Year
    , int Month
    , int? Created
    , int? Updated
    , int? Unchanged
    , int? AllocatedEmployees
    , int? ClosedEmployees
    , int? NewlyClosedEmployees
    , bool? IsClosed
    , decimal? FinalEmployeeAmountDue
);

public sealed class CafeteriaServicesClient(HttpClient http)
{
    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetEventsAsync
    (
          long afterSequence
        , int take
        , CancellationToken cancellationToken = default
    )
    {
        string path = $"api/pos/events?afterSequence={afterSequence}&take={Math.Clamp(take, 1, 500)}";
        return await http.GetFromJsonAsync<CafeteriaServiceEventEnvelope[]>(path, cancellationToken) ?? [];
    }

    public async Task PublishTransactionEventAsync(PosTransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/pos/transactions", transactionEvent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CustomerNotificationPreferenceSnapshot> GetNotificationPreferenceAsync
    (
          int customerId
        , CancellationToken cancellationToken = default
    )
    {
        return await http.GetFromJsonAsync<CustomerNotificationPreferenceSnapshot>
        (
              $"api/pos/customers/{customerId}/notification-preference"
            , cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no notification preference.");
    }

    public async Task<CustomerNotificationPreferenceSnapshot> SetNotificationPreferenceAsync
    (
          int customerId
        , bool weeklySummaryEnabled
        , string? emailAddress
        , CancellationToken cancellationToken = default
    )
    {
        using HttpResponseMessage response = await http.PutAsJsonAsync
        (
              $"api/pos/customers/{customerId}/notification-preference"
            , new
            {
                  WeeklySummaryEnabled = weeklySummaryEnabled
                , EmailAddress = emailAddress
            }
            , cancellationToken
        );

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerNotificationPreferenceSnapshot>
        (
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no notification preference.");
    }
    public async Task<MonthOperationResult> RetryMonthOpeningAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        return await SendForResultAsync<MonthOperationResult>(
            HttpMethod.Post,
            $"api/month-opening/run?year={year}&month={month}",
            new { },
            cancellationToken);
    }

    public async Task<MonthOperationResult> RecalculateMonthAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        return await SendForResultAsync<MonthOperationResult>(
            HttpMethod.Post,
            $"api/month-opening/recalculate?year={year}&month={month}",
            new { },
            cancellationToken);
    }

    public async Task<MonthOperationResult> RetryMonthCloseAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        return await SendForResultAsync<MonthOperationResult>(
            HttpMethod.Post,
            $"api/month-closing/retry?year={year}&month={month}",
            new { },
            cancellationToken);
    }

    public async Task EmailDailySalesReportAsync(
        DateOnly date,
        IReadOnlyCollection<string> recipients,
        string attachmentFileName,
        byte[] attachmentContent,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            "api/management/reports/daily-sales/send",
            new
            {
                  Date = date
                , Recipients = recipients
                , AttachmentFileName = attachmentFileName
                , AttachmentContentType = "application/pdf"
                , AttachmentContent = attachmentContent
            },
            cancellationToken);
    }

    public async Task ResendDailySalesReportAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            "api/management/reports/daily-sales/send",
            new { Date = date },
            cancellationToken);
    }

    public async Task ResendManagerWeeklySummaryAsync(
        DateOnly weekStart,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            HttpMethod.Post,
            "api/management/reports/weekly-manager/send",
            new { WeekStart = weekStart },
            cancellationToken);
    }

    private async Task<T> SendForResultAsync<T>(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, path)
        {
            Content = JsonContent.Create(body)
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateServiceExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned an empty response.");
    }

    private async Task SendAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, path)
        {
            Content = JsonContent.Create(body)
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateServiceExceptionAsync(response, cancellationToken);
        }
    }

    private static async Task<InvalidOperationException> CreateServiceExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        string message = string.IsNullOrWhiteSpace(text)
            ? $"Cafeteria Services returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
            : text;

        return new InvalidOperationException(message);
    }

}
