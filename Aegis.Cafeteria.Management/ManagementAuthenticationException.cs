namespace Aegis.Cafeteria.Management;

internal sealed class ManagementAuthenticationException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
