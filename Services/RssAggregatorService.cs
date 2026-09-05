using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BnsNewsRss.Constants;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;
using BnsNewsRss.Models;
using Microsoft.Extensions.Caching.Memory;

namespace BnsNewsRss.Services;

public class RssAggregatorService
{
    private const string MainFeed = "https://sc.bns.lt/rss";
    private const int MaxItems = 40;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _http;
    private readonly TimeSpan _mainFeedCacheDuration = TimeSpan.FromDays(7);
    private readonly ArticleScraperService _scraper;

    public RssAggregatorService(IHttpClientFactory http, IMemoryCache cache, ArticleScraperService scraper)
    {
        _http = http;
        _cache = cache;
        _scraper = scraper;
    }

    public async Task<string> GetCachedWordPressFeedAsync(string topicName)
    {
        if (_cache.TryGetValue($"{CacheKeys.WordPressFeed}_{topicName}", out string cached))
        {
            return cached;
        }

        var xml = await BuildWordPressFeedAsync(topicName);
        _cache.Set($"{CacheKeys.WordPressFeed}_{topicName}", xml, TimeSpan.FromHours(Configuration.FetchIntervalHours));

        return xml;
    }

    public async Task<string> BuildWordPressFeedAsync(string topicName)
    {
        var tuples = await BuildScrapedItemsForTopicAsync(topicName);

        var items = tuples
            .Select(t =>
            {
                var fi = t.Item1;
                var sa = t.Item2;
                fi.Content = sa.Content;
                fi.FeaturedImage = sa.FeaturedImage ?? GetFeaturedImageFromDescription(fi.Description);
                EnsureFallbackImage(fi);
                return fi;
            })
            .OrderByDescending(i => i.PubDate)
            .ToList();

        return BuildWordPressXml(items, topicName);
    }

    private async Task<List<Tuple<FeedItem, ScrapedArticle>>> BuildScrapedItemsForTopicAsync(string topicName)
    {
        var topics = await ReadTopicsAsync();
        var filtered = FilterTopics(topics);
        var allMeta = await GatherAllMetaItemsAsync(filtered);
        var selected = SelectTopItems(allMeta, MaxItems);

        var scrapedTuples = new List<Tuple<FeedItem, ScrapedArticle>>();
        foreach (var item in selected)
        {
            try
            {
                var scraped = await _scraper.ScrapeArticleAsync(item.Link, item.Guid);
                if (scraped.Content?.Contains("<p>") ?? false)
                {
                    var updated = item with
                    {
                        Content = scraped.Content,
                        FeaturedImage = scraped.FeaturedImage ?? GetFeaturedImageFromDescription(item.Description)
                    };
                    EnsureFallbackImage(updated);
                    scrapedTuples.Add(new Tuple<FeedItem, ScrapedArticle>(updated, scraped));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error scraping {item.Link}: {ex.Message}");
            }
        }

        return scrapedTuples.Where(t => t.Item1.MappedCategories.Contains(topicName)).ToList();
    }

    public async Task<Dictionary<string, List<Tuple<FeedItem, ScrapedArticle>>>> BuildScrapedDictionaryForTopicAsync(
        string topicName)
    {
        var list = await BuildScrapedItemsForTopicAsync(topicName);
        var dict = new Dictionary<string, List<Tuple<FeedItem, ScrapedArticle>>> { [topicName] = list };

        return dict;
    }

    public async Task<Dictionary<string, List<Tuple<FeedItem, ScrapedArticle>>>>
        GetCachedBuildScrapedDictionaryForTopicAsync(
            string topicName)
    {
        var cacheKey = $"{CacheKeys.ScrapedArticlesFeed}_{topicName}";

        if (_cache.TryGetValue(cacheKey, out Dictionary<string, List<Tuple<FeedItem, ScrapedArticle>>> cached))
        {
            return cached;
        }

        var list = await BuildScrapedItemsForTopicAsync(topicName);
        var dict = new Dictionary<string, List<Tuple<FeedItem, ScrapedArticle>>> { [topicName] = list };

        _cache.Set(cacheKey, dict, TimeSpan.FromHours(Configuration.FetchIntervalHours));

        return dict;
    }

    // Helpers
    private IEnumerable<Topic> FilterTopics(IEnumerable<Topic> topics)
    {
        var excluded = new[]
        {
            "Visi pranešimai", "RAIT apklausos", "Teisinė sistema",
            "Nacionalinis saugumas", "Spaudos konferencijos",
            "Viešoji komunikacija", "Jaunimas"
        };

        return topics.Where(t => !excluded.Any(e => t.Title.Contains(e, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<List<FeedItem>> GatherAllMetaItemsAsync(IEnumerable<Topic> topics)
    {
        var list = new List<FeedItem>();
        foreach (var topic in topics)
        {
            try
            {
                var items = await ReadTopicItemsMetaAsync(topic);
                list.AddRange(items);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }

        return list;
    }

    private static List<FeedItem> SelectTopItems(List<FeedItem> items, int max)
    {
        var deduped = items
            .GroupBy(i => i.Guid)
            .Select(g => g.First())
            .OrderByDescending(i => i.PubDate)
            .ToList();

        var groupedByCategory = deduped
            .GroupBy(i => i.BnsCategory)
            .Select(g => g.OrderByDescending(i => i.PubDate).ToList())
            .ToList();

        var finalItems = new List<FeedItem>();
        var index = 0;
        while (finalItems.Count < max)
        {
            var addedAny = false;
            foreach (var categoryItems in groupedByCategory)
            {
                if (index < categoryItems.Count)
                {
                    finalItems.Add(categoryItems[index]);
                    addedAny = true;
                    if (finalItems.Count == max)
                    {
                        break;
                    }
                }
            }

            if (!addedAny)
            {
                break;
            }

            index++;
        }

        return finalItems;
    }

    private void EnsureFallbackImage(FeedItem item)
    {
        if (string.IsNullOrEmpty(item.FeaturedImage) || item.FeaturedImage.Contains("sc.bns.lt/img/logo.png"))
        {
            item.FeaturedImage = $"{Configuration.HostUrl}/images/{Random.Shared.Next(1, 6)}.jpg";
        }
    }

    /*
     * <![CDATA[ <img src="https://sc.bns.lt/docs/1/521559/original_Vilmaimaitien.jpg" alt="" />... ]]>
     */
    private string GetFeaturedImageFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var match = Regex.Match(description, @"<img\s+src=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<List<FeedItem>> ReadTopicItemsMetaAsync(Topic topic)
    {
        var xml = SanitizeXml(await DownloadXmlAsync(topic.Url));
        var doc = LoadXmlSafe(xml);

        var items = new List<FeedItem>();
        foreach (var i in doc.Descendants("item"))
        {
            DateTime.TryParseExact(
                i.Element("pubDate")?.Value,
                "ddd, dd MMM yyyy HH:mm:ss zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var pubDate
            );

            var guid = i.Element("guid")?.Value ?? Guid.NewGuid().ToString();
            var title = i.Element("title")?.Value ?? "";
            var link = i.Element("link")?.Value.Trim() ?? "";
            var description = i.Element("description")?.Value ?? "";

            items.Add(new FeedItem(
                title.Trim(),
                link,
                description.Trim(),
                pubDate == default ? DateTime.UtcNow : pubDate,
                guid,
                topic.Title,
                CategoryMapper.MapBnsTopicToCategory(topic.Title)
            ));
        }

        return items;
    }

    private async Task<List<Topic>> ReadTopicsAsync()
    {
        string xml;
        if (!_cache.TryGetValue($"{CacheKeys.WordPressFeed}_{nameof(MainFeed)}", out xml))
        {
            xml = SanitizeXml(await DownloadXmlAsync(MainFeed));
            _cache.Set($"{CacheKeys.WordPressFeed}_{nameof(MainFeed)}", xml, _mainFeedCacheDuration);
        }

        var doc = LoadXmlSafe(xml);

        return doc.Descendants("item")
            .Select(i => new Topic(
                i.Element("title")?.Value.Trim() ?? "",
                i.Element("link")?.Value.Trim() ?? ""
            ))
            .Where(t => !string.IsNullOrEmpty(t.Url) && t.Url.EndsWith("/rss", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static XDocument LoadXmlSafe(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            CheckCharacters = false,
            IgnoreWhitespace = false
        };

        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static string SanitizeXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return "";
        }

        return xml.Replace("&nbsp;", " ").Replace("&laquo;", "«").Replace("&raquo;", "»");
    }

    private async Task<string> DownloadXmlAsync(string url)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return await client.GetStringAsync(url);
    }

    private static string BuildWordPressXml(List<FeedItem> items, string topicName)
    {
        var sb = new StringBuilder();

        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<rss version=""2.0""
 xmlns:content=""http://purl.org/rss/1.0/modules/content/""
 xmlns:dc=""http://purl.org/dc/elements/1.1/""
 xmlns:atom=""http://www.w3.org/2005/Atom""
 xmlns:media=""http://search.yahoo.com/mrss/"">");

        sb.AppendLine("<channel>");
        sb.AppendLine($"<title>{topicName}</title>");
        sb.AppendLine("<link>https://sc.bns.lt</link>");
        sb.AppendLine($"<description>{topicName}</description>");
        sb.AppendLine($"<lastBuildDate>{DateTime.UtcNow:R}</lastBuildDate>");
        sb.AppendLine("<language>lt</language>");
        sb.AppendLine(@"<atom:link href=""https://yourdomain.lt/feed"" rel=""self"" type=""application/rss+xml"" />");

        foreach (var item in items.Where(i => i.MappedCategories.Contains(topicName)))
        {
            sb.AppendLine("<item>");
            sb.AppendLine($"<title><![CDATA[{item.Title}]]></title>");
            sb.AppendLine($"<guid isPermaLink=\"false\">{item.Guid}</guid>");
            sb.AppendLine($"<pubDate>{item.PubDate:R}</pubDate>");
            foreach (var category in item.MappedCategories)
            {
                sb.AppendLine($"<category><![CDATA[{category}]]></category>");
            }

            sb.AppendLine($"<description><![CDATA[{item.Description}]]></description>");
            sb.AppendLine($"<content:encoded><![CDATA[{item.Content}]]></content:encoded>");
            if (!string.IsNullOrEmpty(item.FeaturedImage) && !item.FeaturedImage.Contains("sc.bns.lt/img/logo.png"))
            {
                var type = item.FeaturedImage.Split(".")[^1].ToLower();
                if (type == "jpg")
                {
                    type = "jpeg";
                }

                sb.AppendLine($@"<enclosure url=""{item.FeaturedImage}"" length=""0"" medium=""image/{type}"" />");
            }

            sb.AppendLine("</item>");
        }

        sb.AppendLine("</channel>");
        sb.AppendLine("</rss>");

        return sb.ToString();
    }
}