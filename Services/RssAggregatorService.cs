using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using BnsNewsRss.Models;
using BnsNewsRss.Keys;
using BnsNewsRss.Mappers;

namespace BnsNewsRss.Services;

public class RssAggregatorService
{
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ArticleScraperService _scraper;

    private const string MainFeed = "https://sc.bns.lt/rss";
    private const int MaxItems = 40;

    public RssAggregatorService(IHttpClientFactory http, IMemoryCache cache, ArticleScraperService scraper)
    {
        _http = http;
        _cache = cache;
        _scraper = scraper;
    }

    public async Task<string> GetCachedWordPressFeedAsync(string topicName)
    {
        if (_cache.TryGetValue($"{CacheKeys.WordPressFeed}_{topicName}", out string cached))
            return cached;

        var xml = await BuildWordPressFeedAsync(topicName);
        _cache.Set($"{CacheKeys.WordPressFeed}_{topicName}", xml, TimeSpan.FromHours(12));
        
        return xml;
    }
    
    public async Task<string> BuildWordPressFeedAsync(string topicName)
    {
        var topics = await ReadTopicsAsync();
        var allItems = new List<FeedItem>();
    
        foreach (var topic in topics)
        {
            try
            {
                //don't read these topics
                if (topic.Title.Contains("Visi pranešimai") || topic.Title.Contains("RAIT apklausos") || topic.Title.Contains("Teisinė sistema") || topic.Title.Contains("Nacionalinis saugumas") || topic.Title.Contains("Spaudos konferencijos") || topic.Title.Contains("Viešoji komunikacija") || topic.Title.Contains("Jaunimas"))
                {
                    continue;
                }
                
                var items = await ReadTopicItemsMetaAsync(topic); // Only metadata, no scraping
                allItems.AddRange(items);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }
        }
        
        //clusterfuck
        //TODO optimize by custom category selection priority list if this will be needed
        
        var deduped = allItems
            .GroupBy(i => i.Guid)
            .Select(g => g.First())
            .OrderByDescending(i => i.PubDate)
            .Take(MaxItems)
            .ToList();

         var groupedByCategory = deduped
             .GroupBy(i => i.BnsCategory)
             .Select(g => g
                 .OrderByDescending(i => i.PubDate)
                 .ToList())
             .ToList();

        var finalItems = new List<FeedItem>();
         int index = 0;
        
         while (finalItems.Count < MaxItems)
         {
             bool addedAny = false;
        
             foreach (var categoryItems in groupedByCategory)
             {
                 if (index < categoryItems.Count)
                 {
                     finalItems.Add(categoryItems[index]);
                     addedAny = true;
        
                     if (finalItems.Count == MaxItems)
                         break;
                 }
             }
        
             if (!addedAny)
                 break; // no more items in any category
        
             index++;
         }
    
        // Now scrape only the selected items
        var scrapedItems = new List<FeedItem>();
        foreach (var item in finalItems)
        {
            var scraped = await _scraper.ScrapeArticleAsync(item.Link, item.Guid);

            if (scraped.Content?.Contains("<p>") ?? false)
            {
                scrapedItems.Add(item with
                {
                    Content = scraped.Content,
                    FeaturedImage = scraped.FeaturedImage ?? GetFeaturedImageFromDescription(item.Description)
                });
            }
        }
        
        scrapedItems.Sort( (a, b) => b.PubDate.CompareTo(a.PubDate) );
    
        return BuildWordPressXml(scrapedItems, topicName);
    }
    
    /*
     * <![CDATA[ <img src="https://sc.bns.lt/docs/1/521559/original_Vilmaimaitien.jpg" alt="" />Metų pabaiga daugeliui iš mūsų atneša ne tik šventinę nuotaiką, bet ir nuovargį, įtampą, vidinį spaudimą „dar spėti“, „užbaigti“, „padaryti geriau“. Taip pat Naujųjų metų pradžia kviečia atsinaujinti – atsisakyti žalingų įpročių, susidaryti sąrašą da... ]]>
     */
    
    private string GetFeaturedImageFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        
        var match = Regex.Match(description, @"<img\s+src=""([^""]+)""");
        
        return match.Success ? match.Groups[1].Value : null;
    }
    
    // New method: only reads metadata, no scraping
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
                Title: title.Trim(),
                Link: link,
                Description: description.Trim(),
                PubDate: pubDate == default ? DateTime.UtcNow : pubDate,
                Guid: guid,
                BnsCategory: topic.Title,
                MappedCategories: CategoryMapper.MapBnsTopicToCategory(topic.Title),
                Content: "",
                FeaturedImage: null
            ));
        }
    
        return items;
    }

    private async Task<List<Topic>> ReadTopicsAsync()
    {
        var xml = SanitizeXml(await DownloadXmlAsync(MainFeed));
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
        if (string.IsNullOrWhiteSpace(xml)) return "";
        
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
            sb.AppendLine($"<link>{item.Link}</link>");
            sb.AppendLine($"<guid isPermaLink=\"false\">{item.Guid}</guid>");
            sb.AppendLine($"<pubDate>{item.PubDate:R}</pubDate>");
            foreach (var category in item.MappedCategories)
            {
                sb.AppendLine($"<category><![CDATA[{category}]]></category>");
            }
            //sb.AppendLine($"<category><![CDATA[{item.Category}]]></category>");
            sb.AppendLine($"<description><![CDATA[{item.Description}]]></description>");
            sb.AppendLine($"<content:encoded><![CDATA[{item.Content}]]></content:encoded>");
            if (!string.IsNullOrEmpty(item.FeaturedImage))
            {
                string type = item.FeaturedImage.Split(".")[^1].ToLower();
                if (type == "jpg") type = "jpeg";
                
                sb.AppendLine($@"<enclosure url=""{item.FeaturedImage}"" length=""0"" medium=""image/{type}"" />");
            }
            sb.AppendLine("</item>");
        }

        sb.AppendLine("</channel>");
        sb.AppendLine("</rss>");

        return sb.ToString();
    }
}
