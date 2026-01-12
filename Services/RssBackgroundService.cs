using BnsNewsRss.Constants;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Caching.Memory;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
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
                foreach (var category in CategoryMapper.AllCategories)
                {
                    var xml = await _aggregator.BuildWordPressFeedAsync(category);
                    
                    _cache.Set($"{CacheKeys.WordPressFeed}_{category}", xml, TimeSpan.FromHours(Configuration.FetchInterval));
                }
                
                _state.LastRefreshUtc = DateTime.UtcNow;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"Error while building WordPress feed from RSS Feed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(Configuration.FetchInterval), stoppingToken);
        }
    }
}