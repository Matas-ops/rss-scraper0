using System.Net;

namespace BnsNewsRss.Exceptions;

public sealed class FacebookApiException : Exception
{
    public FacebookApiException(
        HttpStatusCode statusCode,
        string responseBody)
        : base($"Facebook API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }
}