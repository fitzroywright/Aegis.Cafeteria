namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class ManagementEventSignalRWorker : BackgroundService
{
    private const string EventsAvailableMethod = "EventsAvailable";

    private readonly CafeteriaServicesOptions options;
    private readonly ManagementEventWakeSignal wakeSignal;
    private readonly ILogger<ManagementEventSignalRWorker> logger;

    public ManagementEventSignalRWorker
    (
          CafeteriaServicesOptions options
        , ManagementEventWakeSignal wakeSignal
        , ILogger<ManagementEventSignalRWorker> logger
    )
    {
        this.options = options;
        this.wakeSignal = wakeSignal;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync
    (
        CancellationToken stoppingToken
    )
    {
        if (!this.options.Enabled || !this.options.RealtimeEnabled)
        {
            return;
        }

        string hubUrl = BuildHubUrl
        (
              this.options.BaseUrl
            , this.options.HubPath
        );

        HubConnection connection = BuildConnection(hubUrl);

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
                    , "Cafeteria Services real-time connection closed; durable polling remains active"
                );
            }

            this.wakeSignal.Pulse();
            return Task.CompletedTask;
        };

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (connection.State == HubConnectionState.Disconnected)
                    {
                        await connection.StartAsync(stoppingToken);

                        this.logger.LogInformation
                        (
                              "Connected to Cafeteria Services real-time hub at {HubUrl}"
                            , hubUrl
                        );

                        this.wakeSignal.Pulse();
                    }

                    await Task.Delay
                    (
                          TimeSpan.FromSeconds(this.options.RealtimeReconnectSeconds)
                        , stoppingToken
                    );
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

                    try
                    {
                        await Task.Delay
                        (
                              TimeSpan.FromSeconds(this.options.RealtimeReconnectSeconds)
                            , stoppingToken
                        );
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown. BackgroundService cancellation must not surface as an application error.
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // SignalR may observe the host cancellation while disposing. This is also normal shutdown.
            }
        }
    }

    private HubConnection BuildConnection
    (
        string hubUrl
    )
    {
        HubConnectionBuilder builder = new HubConnectionBuilder();

        builder.WithUrl
        (
              hubUrl
            , connectionOptions =>
            {
                if (!string.IsNullOrWhiteSpace(this.options.ApiKey))
                {
                    connectionOptions.Headers["X-Cafeteria-Api-Key"] = this.options.ApiKey;
                }
            }
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

    private static string BuildHubUrl
    (
          string baseUrl
        , string hubPath
    )
    {
        string normalizedBaseUrl = baseUrl.TrimEnd('/');
        string normalizedHubPath = hubPath.StartsWith('/')
            ? hubPath
            : $"/{hubPath}";

        return $"{normalizedBaseUrl}{normalizedHubPath}";
    }
}
