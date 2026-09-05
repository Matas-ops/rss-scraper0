using System.Net;
using System.Text;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using BnsNewsRss.Services;
using DotNetEnv;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace BnsNewsRss;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var envFile = Path.Combine(builder.Environment.ContentRootPath, ".env");
        if (File.Exists(envFile))
        {
            Env.Load(envFile);
        }

        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddHttpClient();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ArticleScraperService>();
        builder.Services.AddSingleton<RssAggregatorService>();
        builder.Services.AddSingleton<RssHealthState>();
        builder.Services.AddHostedService<RssBackgroundService>();
        builder.Services.AddHttpClient<FacebookPageService>();
        builder.Services.AddHostedService<FacebookAutoPosterService>();

        var app = builder.Build();

        app.MapGet("/health", (IMemoryCache cache, RssHealthState state) =>
        {
            var cached = cache.TryGetValue($"{CacheKeys.WordPressFeed}_Aktualijos", out _);

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
                return Results.Text(xml, "application/rss+xml", Encoding.UTF8);
            });
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath, "data")),
            RequestPath = "/images"
        });

        app.Run();
    }
}