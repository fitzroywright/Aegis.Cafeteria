namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Security;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class ManagementEventSignalRWorker : BackgroundService
{
    private const string EventsAvailableMethod = "EventsAvailable";

    private readonly CafeteriaServicesOptions options;
    private readonly PosServicesSession servicesSession;
    private readonly ManagementEventWakeSignal wakeSignal;
    private readonly ILogger<ManagementEventSignalRWorker> logger;

    public ManagementEventSignalRWorker
    (
          CafeteriaServicesOptions options
        , PosServicesSession servicesSession
        , ManagementEventWakeSignal wakeSignal
        , ILogger<ManagementEventSignalRWorker> logger
    )
    {
        this.options = options;
        this.servicesSession = servicesSession;
        this.wakeSignal = wakeSignal;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.options.Enabled || !this.options.RealtimeEnabled)
        {
            return;
        }

        string hubUrl = BuildHubUrl(this.options.BaseUrl, this.options.HubPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            string? token = this.servicesSession.Token;
            if (string.IsNullOrWhiteSpace(token))
            {
                await DelayAsync(stoppingToken);
                continue;
            }

            await using HubConnection connection = BuildConnection(hubUrl, token);
            RegisterHandlers(connection, hubUrl, stoppingToken);

            try
            {
                await connection.StartAsync(stoppingToken);
                this.logger.LogInformation
                (
                      "Connected to Cafeteria Services real-time hub at {HubUrl} using the POS session"
                    , hubUrl
                );
                this.wakeSignal.Pulse();

                while (!stoppingToken.IsCancellationRequested
                    && connection.State != HubConnectionState.Disconnected
                    && string.Equals(token, this.servicesSession.Token, StringComparison.Ordinal))
                {
                    await DelayAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.logger.LogWarning
                (
                      exception
                    , "Cafeteria Services real-time connection unavailable; POS will continue using durable polling"
                );
                await DelayAsync(stoppingToken);
            }
        }
    }

    private HubConnection BuildConnection(string hubUrl, string token)
    {
        HubConnectionBuilder builder = new();
        builder.WithUrl
        (
              hubUrl
            , connectionOptions => connectionOptions.Headers["X-Aegis-POS-Token"] = token
        );
        builder.WithAutomaticReconnect
        (
            [
                  TimeSpan.Zero
                , TimeSpan.FromSeconds(2)
                , TimeSpan.FromSeconds(5)
                , TimeSpan.FromSeconds(10)
            ]
        );
        return builder.Build();
    }

    private void RegisterHandlers(HubConnection connection, string hubUrl, CancellationToken stoppingToken)
    {
        connection.On<long, int, int>
        (
              EventsAvailableMethod
            , (sequence, customerId, eventKind) =>
            {
                this.logger.LogDebug
                (
                      "Cafeteria Services announced event sequence {Sequence} for customer {CustomerId}, kind {EventKind}"
                    , sequence
                    , customerId
                    , eventKind
                );
                this.wakeSignal.Pulse();
            }
        );

        connection.Reconnected += connectionId =>
        {
            this.logger.LogInformation
            (
                  "Cafeteria Services real-time connection restored. ConnectionId={ConnectionId}"
                , connectionId
            );
            this.wakeSignal.Pulse();
            return Task.CompletedTask;
        };

        connection.Closed += exception =>
        {
            if (exception is not null && !stoppingToken.IsCancellationRequested)
            {
                this.logger.LogWarning
                (
                      exception
                    , "Cafeteria Services real-time connection closed; durable polling remains active. Hub={HubUrl}"
                    , hubUrl
                );
            }
            this.wakeSignal.Pulse();
            return Task.CompletedTask;
        };
    }

    private async Task DelayAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(this.options.RealtimeReconnectSeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static string BuildHubUrl(string baseUrl, string hubPath)
    {
        string normalizedBaseUrl = baseUrl.TrimEnd('/');
        string normalizedHubPath = hubPath.StartsWith('/') ? hubPath : $"/{hubPath}";
        return $"{normalizedBaseUrl}{normalizedHubPath}";
    }
}
