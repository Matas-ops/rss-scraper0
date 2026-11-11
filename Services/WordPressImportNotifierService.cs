using BnsNewsRss.Constants;

namespace BnsNewsRss.Services;

/// <summary>
///     Tells the WordPress site (RSS Category Importer plugin) that the feeds were refreshed, so it imports
///     the new items right away. The call is asynchronous on the WordPress side (async=1: it answers 202 and
///     imports in the background), so this returns quickly. Failures are logged and never stop the feed service.
/// </summary>
public class WordPressImportNotifierService
{
    public const string HttpClientName = "WordPressImport";
    private const string TokenHeader = "X-RSSI-Token";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<WordPressImportNotifierService> _logger;
    private readonly string? _token;
    private readonly Uri? _url;

    public WordPressImportNotifierService(IHttpClientFactory http, ILogger<WordPressImportNotifierService> logger)
    {
        _http = http;
        _logger = logger;

        var token = Environment.GetEnvironmentVariable(Configuration.WordPressImportTokenVariable)?.Trim();
        _token = string.IsNullOrEmpty(token) ? null : token;

        var url = Environment.GetEnvironmentVariable(Configuration.WordPressImportUrlVariable)?.Trim();
        _url = BuildUrl(string.IsNullOrEmpty(url) ? Configuration.WordPressImportUrl : url);

        if (_token is null)
            _logger.LogWarning("{Variable} is not set: WordPress will not be told to import after feed refreshes.",
                Configuration.WordPressImportTokenVariable);
        if (_url is null)
            _logger.LogWarning("{Variable} is not a valid http(s) URL: WordPress will not be told to import.",
                Configuration.WordPressImportUrlVariable);
    }

    /// <summary>Whether a token and a valid URL are configured.</summary>
    public bool IsConfigured => _token is not null && _url is not null;

    /// <summary>
    ///     Calls the import endpoint with async=1. Never throws (except when <paramref name="cancellationToken" /> is
    ///     cancelled).
    /// </summary>
    /// <returns>True when WordPress accepted the request.</returns>
    public async Task<bool> NotifyAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _url);
            // Header instead of ?token=: the token does not end up in URLs / access logs.
            request.Headers.TryAddWithoutValidation(TokenHeader, _token);

            using var response = await _http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("WordPress import triggered ({StatusCode}).", (int)response.StatusCode);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Length > 300)
                body = body[..300];
            _logger.LogWarning("WordPress import request failed: {StatusCode} {Body}", (int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning("WordPress import request failed: {Message}", ex.Message);
        }

        return false;
    }

    private static Uri? BuildUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;

        var builder = new UriBuilder(uri);
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(query) ? "async=1" : $"{query}&async=1";
        return builder.Uri;
    }
}