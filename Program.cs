using System.Net;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using BnsNewsRss.Services;
using Microsoft.Extensions.Caching.Memory;

namespace BnsNewsRss;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ArticleScraperService>();
        builder.Services.AddSingleton<RssAggregatorService>();
        builder.Services.AddSingleton<RssHealthState>();
        builder.Services.AddHostedService<RssBackgroundService>();

        var app = builder.Build();

        app.MapGet("/health", (IMemoryCache cache, RssHealthState state) =>
        {
            var cached = cache.TryGetValue(CacheKeys.WordPressFeed, out _);
            return Results.Json(new
            {
                status = HttpStatusCode.OK,
                cachedFeed = cached,
                lastRefreshUtc = state.LastRefreshUtc,
                uptimeUtc = DateTime.UtcNow
            });
        });

        foreach (var category in CategoryMapper.AllCategories)
        {
            app.MapGet($"/{category.ToLower()}", async (RssAggregatorService rss) =>
            {
                var xml = await rss.GetCachedWordPressFeedAsync(category);
                
                return Results.Text(xml, "application/rss+xml", System.Text.Encoding.UTF8);
            });
        }

        app.Run();
    }
}