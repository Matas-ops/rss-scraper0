using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BnsNewsRss.Services;

public class FacebookAutoPosterService : BackgroundService
{
    private readonly IMemoryCache _cache;

    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(12);
    private readonly FacebookPageService _facebookPageService;
    private readonly ILogger<FacebookAutoPosterService> _logger;
    private readonly string? _pageAccessToken;
    private readonly string? _pageId;
    private readonly TimeSpan _postCooldown = TimeSpan.FromDays(30);
    private readonly RssAggregatorService _rssAggregator;

    public FacebookAutoPosterService(
        IMemoryCache cache,
        RssAggregatorService rssAggregator,
        FacebookPageService facebookPageService,
        IConfiguration config,
        ILogger<FacebookAutoPosterService> logger)
    {
        _cache = cache;
        _rssAggregator = rssAggregator;
        _facebookPageService = facebookPageService;
        _logger = logger;

        _pageId = config["FACEBOOK_PAGE_ID"] ?? config["facebook:pageId"];
        _pageAccessToken = config["FACEBOOK_PAGE_ACCESS_TOKEN"] ?? config["facebook:pageAccessToken"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_pageId) || string.IsNullOrWhiteSpace(_pageAccessToken))
        {
            _logger.LogWarning(
                "Facebook auto-poster is disabled because FACEBOOK_PAGE_ID or FACEBOOK_PAGE_ACCESS_TOKEN is missing.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await PublishRandomAvailableArticleAsync(stoppingToken);
                _logger.LogInformation($"Facebook post result: {result?.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish a random article to Facebook.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    public async Task<FacebookPostResult?> PublishRandomAvailableArticleAsync(
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_pageId) || string.IsNullOrWhiteSpace(_pageAccessToken))
        {
            return null;
        }

        var candidates = await GetEligibleCandidatesAsync();
        if (candidates.Count == 0)
        {
            return null;
        }

        var selected = candidates[Random.Shared.Next(candidates.Count)];

        var message = $"{selected.Title}\n\n{selected.Link}";

        var result = await _facebookPageService.CreatePostAsync(
            _pageId,
            _pageAccessToken,
            message,
            selected.FeaturedImage,
            cancellationToken);

        _cache.Set(
            CacheKeys.FacebookPosted(selected.Guid),
            DateTime.UtcNow,
            TimeSpan.FromDays(30));

        return result;
    }

    private async Task<List<FeedItem>> GetEligibleCandidatesAsync()
    {
        var items = new List<FeedItem>();

        foreach (var category in CategoryMapper.AllCategories)
        {
            var dict = await _rssAggregator.GetCachedBuildScrapedDictionaryForTopicAsync(category);
            if (!dict.TryGetValue(category, out var topicItems))
            {
                continue;
            }

            foreach (var tuple in topicItems)
            {
                var item = tuple.Item1;

                if (IsPostedRecently(item.Guid))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Link))
            .OrderBy(_ => Guid.NewGuid())
            .ToList();
    }

    private bool IsPostedRecently(string guid)
    {
        if (!_cache.TryGetValue(CacheKeys.FacebookPosted(guid), out DateTime lastPosted))
        {
            return false;
        }

        return DateTime.UtcNow - lastPosted < _postCooldown;
    }
}