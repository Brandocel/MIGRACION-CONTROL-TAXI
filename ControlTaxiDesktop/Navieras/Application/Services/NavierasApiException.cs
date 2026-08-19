namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class NavierasApiException : InvalidOperationException
{
    public NavierasApiException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
