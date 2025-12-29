using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using BnsNewsRss.Keys;
using BnsNewsRss.Models;
using BnsNewsRss.Services;

namespace BnsNewsRss.Services;

public class RssBackgroundService : BackgroundService
{
    private readonly RssAggregatorService _aggregator;
    private readonly IMemoryCache _cache;
    private readonly RssHealthState _state;

    public RssBackgroundService(RssAggregatorService aggregator, IMemoryCache cache, RssHealthState state)
    {
        _aggregator = aggregator;
        _cache = cache;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var xml = await _aggregator.BuildWordPressFeedAsync();
                _cache.Set(CacheKeys.WordPressFeed, xml, TimeSpan.FromHours(5));
                _state.LastRefreshUtc = DateTime.UtcNow;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"Error while building WordPress feed from RSS Feed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(5), stoppingToken);
        }
    }
}