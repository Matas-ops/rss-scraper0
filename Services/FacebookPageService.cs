using System.Net.Http.Headers;
using System.Text.Json;
using BnsNewsRss.Exceptions;
using BnsNewsRss.Models;

namespace BnsNewsRss.Services;

public class FacebookPageService
{
    private readonly HttpClient _httpClient;

    public FacebookPageService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FacebookPostResult> CreatePostAsync(
        string pageId,
        string pageAccessToken,
        string message,
        string? imageUrl = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = string.IsNullOrWhiteSpace(imageUrl)
            ? $"https://graph.facebook.com/v26.0/{pageId}/feed"
            : $"https://graph.facebook.com/v26.0/{pageId}/photos";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", pageAccessToken);

        var payload = new Dictionary<string, string>
        {
            ["message"] = message
        };

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            payload["url"] = imageUrl;
        }

        request.Content = new FormUrlEncodedContent(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FacebookApiException(
                response.StatusCode,
                responseBody);
        }

        var result = JsonSerializer.Deserialize<FacebookPostResult>(responseBody);

        if (result is null || string.IsNullOrWhiteSpace(result.Id))
        {
            throw new FacebookApiException(
                response.StatusCode,
                $"Facebook returned an unexpected response: {responseBody}");
        }

        return result;
    }
}