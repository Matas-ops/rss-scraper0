using System.Net;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using BnsNewsRss.Services;
using dotenv.net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace BnsNewsRss;

public class Program
{
    public static void Main(string[] args)
    {
        // Local runs: variables from a .env file (searched from the build output folder up to the project folder).
        // Production: the variables defined in the environment win - an existing variable is never overwritten,
        // and a missing .env file is fine.
        DotEnv.Load(new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 4, overwriteExistingVars: false, trimValues: true));

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient(WordPressImportNotifierService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BnsNewsRss/1.0");
        });
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ArticleScraperService>();
        builder.Services.AddSingleton<RssAggregatorService>();
        builder.Services.AddSingleton<RssHealthState>();
        builder.Services.AddSingleton<WordPressImportNotifierService>();
        builder.Services.AddHostedService<RssBackgroundService>();

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
                
                return Results.Text(xml, "application/rss+xml", System.Text.Encoding.UTF8);
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