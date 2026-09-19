namespace Aegis.Cafeteria.Application.Messaging;

/// <summary>
/// Adapter to the organization's messaging infrastructure. Cafeteria identifies the employee and supplies message content;
/// the external messaging service resolves configured delivery channels.
/// </summary>
public interface ICafeteriaMessagingGateway
{
    Task SendAsync(CafeteriaEmployeeMessage message, CancellationToken cancellationToken = default);
}

public sealed record CafeteriaEmployeeMessage(
    Guid MessageId,
    int EmployeeId,
    string Category,
    string Subject,
    string TextBody,
    DateTimeOffset CreatedAt,
    string? CorrelationId = null);
