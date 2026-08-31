namespace Aegis.Cafeteria.Services.Realtime;

using Aegis.Cafeteria.Domain.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

public sealed class ServiceEventRealtimePublisher
{
    private readonly IHubContext<ManagementEventsHub> hubContext;
    private readonly ILogger<ServiceEventRealtimePublisher> logger;

    public ServiceEventRealtimePublisher
    (
          IHubContext<ManagementEventsHub> hubContext
        , ILogger<ServiceEventRealtimePublisher> logger
    )
    {
        this.hubContext = hubContext;
        this.logger = logger;
    }

    public async Task PublishAsync
    (
          CafeteriaServiceEventEnvelope serviceEvent
        , CancellationToken cancellationToken = default
    )
    {
        try
        {
            await this.hubContext.Clients.All.SendAsync
            (
                  ManagementEventsHub.EventsAvailableMethod
                , serviceEvent.Sequence
                , serviceEvent.CustomerId
                , (int)serviceEvent.Kind
                , cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning
            (
                  exception
                , "Could not publish the real-time notification for Cafeteria service event {Sequence}; durable polling will deliver it"
                , serviceEvent.Sequence
            );
        }
    }

}
