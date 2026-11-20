namespace NginxSslManager.Api.Common;

/// <summary>
/// An expected, user-facing error. The message is safe to display in the UI.
/// </summary>
public class ApiException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message) : ApiException(message, StatusCodes.Status404NotFound);

public sealed class NginxException(string message, string? output = null) : ApiException(
    string.IsNullOrWhiteSpace(output) ? message : $"{message}\n{output}", StatusCodes.Status502BadGateway)
{
    public string? Output { get; } = output;
}
