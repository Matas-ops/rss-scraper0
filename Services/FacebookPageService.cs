using System.Net.Http.Headers;
using System.Text.Json;
using BnsNewsRss.Exceptions;
using BnsNewsRss.Models;

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
        CancellationToken cancellationToken = default)
    {
        var url = $"https://graph.facebook.com/v26.0/{pageId}/feed";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", pageAccessToken);

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["message"] = message
            });

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

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