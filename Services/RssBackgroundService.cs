using BnsNewsRss.Constants;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BnsNewsRss.Services;

public class RssBackgroundService : BackgroundService
{
    private readonly RssAggregatorService _aggregator;
    private readonly IMemoryCache _cache;
    private readonly RssHealthState _state;
    private readonly WordPressImportNotifierService _wordPress;

    public RssBackgroundService(RssAggregatorService aggregator, IMemoryCache cache, RssHealthState state,
        WordPressImportNotifierService wordPress)
    {
        _aggregator = aggregator;
        _cache = cache;
        _state = state;
        _wordPress = wordPress;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshed = false;
            try
            {
                foreach (var category in CategoryMapper.AllCategories)
                {
                    var xml = await _aggregator.BuildWordPressFeedAsync(category);

                    _cache.Set($"{CacheKeys.WordPressFeed}_{category}", xml,
                        TimeSpan.FromHours(Configuration.FetchIntervalHours));
                }

                _state.LastRefreshUtc = DateTime.UtcNow;
                refreshed = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error while building WordPress feed from RSS Feed: {ex.Message}");
            }

            // After app start (first run) and after every refresh: let WordPress import the new items now.
            if (refreshed)
                await _wordPress.NotifyAsync(stoppingToken);

            await Task.Delay(TimeSpan.FromHours(Configuration.FetchIntervalHours), stoppingToken);
        }
    }
}