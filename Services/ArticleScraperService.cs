using System.Net;
using System.Net.Http.Headers;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using BnsNewsRss.Keys;
using BnsNewsRss.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace BnsNewsRss.Services;

public class ArticleScraperService
{
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;

    private static readonly SemaphoreSlim _semaphore = new(2);
    private static DateTime _lastRequest = DateTime.MinValue;

    public ArticleScraperService(IHttpClientFactory http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<ScrapedArticle> ScrapeArticleAsync(string url, string guid)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new ScrapedArticle("", null);

        // Return cached article if exists
        if (_cache.TryGetValue(CacheKeys.Article(guid), out ScrapedArticle cached))
            return cached;

        // Rate limit 2 requests/sec
        await _semaphore.WaitAsync();
        try
        {
            var diff = DateTime.UtcNow - _lastRequest;
            if (diff.TotalMilliseconds < 500)
                await Task.Delay(500 - (int)diff.TotalMilliseconds);

            //sorry
            var client = _http.CreateClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("abc", "1.0"));
            var html = await client.GetStringAsync(url);

            _lastRequest = DateTime.UtcNow;

            var content = ParseArticleContent(html);
            content = CleanHtmlForWordPress(content);

            var featured = ExtractFeaturedImage(html);

            var article = new ScrapedArticle(content, featured);
            _cache.Set(CacheKeys.Article(guid), article, TimeSpan.FromDays(7));

            return article;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string ParseArticleContent(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();

        var bodyNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'sc-item-body')]");

        if (bodyNode == null)
            return "";

        // IMPORTANT: only direct children, not Descendants()
        foreach (var node in bodyNode.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
        {
            switch (node.Name)
            {
                case "p":
                    var text = CleanInner(node);
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine($"<p>{text}</p>");
                    break;

                case "h1":
                case "h2":
                case "h3":
                    sb.AppendLine($"<{node.Name}>{CleanInner(node)}</{node.Name}>");
                    break;

                case "blockquote":
                    sb.AppendLine($"<blockquote>{CleanInner(node)}</blockquote>");
                    break;

                case "figure":
                    AppendFigure(node, sb);
                    break;
                
                case "ul":
                case "ol":
                    AppendList(node, sb);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    private static string CleanInner(HtmlNode node)
    {
        var sb = new StringBuilder();

        foreach (var child in node.ChildNodes)
            AppendInlineNode(child, sb);

        var html = WebUtility.HtmlDecode(sb.ToString());
        html = Regex.Replace(html, @"\s+", " ").Trim();

        return html;
    }
    
    private static void AppendInlineNode(HtmlNode node, StringBuilder sb)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                sb.Append(node.InnerText);
                break;

            case HtmlNodeType.Element:
                switch (node.Name)
                {
                    case "strong":
                        sb.Append("<strong>");
                        foreach (var c in node.ChildNodes)
                            AppendInlineNode(c, sb);
                        sb.Append("</strong>");
                        break;

                    case "em":
                    case "i":
                        sb.Append("<em>");
                        foreach (var c in node.ChildNodes)
                            AppendInlineNode(c, sb);
                        sb.Append("</em>");
                        break;

                    case "a":
                        AppendLink(node, sb);
                        break;

                    default:
                        // unwrap span, p, etc. but keep content
                        foreach (var c in node.ChildNodes)
                            AppendInlineNode(c, sb);
                        break;
                }
                break;
        }
    }
    
    private static void AppendLink(HtmlNode node, StringBuilder sb)
    {
        var href = node.GetAttributeValue("href", null);
        if (string.IsNullOrWhiteSpace(href))
            return;

        var isHttp =
            href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var isMailto =
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

        // Allow only http(s) and mailto
        if (!isHttp && !isMailto)
            return;

        sb.Append("<a href=\"");
        sb.Append(WebUtility.HtmlEncode(href));
        sb.Append("\"");

        var target = node.GetAttributeValue("target", null);
        if (!string.IsNullOrWhiteSpace(target))
            sb.Append($" target=\"{target}\"");

        sb.Append(" rel=\"noopener noreferrer\">");

        foreach (var c in node.ChildNodes)
            AppendInlineNode(c, sb);

        sb.Append("</a>");
    }
    
    private static void AppendList(HtmlNode listNode, StringBuilder sb)
    {
        var tag = listNode.Name; // ul or ol
        sb.AppendLine($"<{tag}>");

        foreach (var li in listNode.SelectNodes("./li") ?? Enumerable.Empty<HtmlNode>())
        {
            var itemSb = new StringBuilder();

            foreach (var child in li.ChildNodes)
                AppendInlineNode(child, itemSb);

            var text = WebUtility.HtmlDecode(itemSb.ToString()).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine($"<li>{text}</li>");
        }

        sb.AppendLine($"</{tag}>");
    }

    private static void AppendFigure(HtmlNode figure, StringBuilder sb)
    {
        var img = figure.SelectSingleNode(".//img");
        var caption = figure.SelectSingleNode(".//figcaption");

        if (img != null)
        {
            var src = img.GetAttributeValue("src", "");
            if (!string.IsNullOrWhiteSpace(src))
            {
                sb.AppendLine($"<figure>");
                sb.AppendLine($"<img src=\"{src}\" alt=\"\" />");

                if (caption != null)
                    sb.AppendLine($"<figcaption>{caption.InnerText.Trim()}</figcaption>");

                sb.AppendLine($"</figure>");
            }
        }
    }
    
    private static string CleanHtmlForWordPress(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script & style safely
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        return doc.DocumentNode.InnerHtml.Trim();
    }

    private static string? ExtractFeaturedImage(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var imgNode = doc.DocumentNode.SelectSingleNode("//figure[contains(@class,'sc-item-logo')]//img");
        if (imgNode != null && imgNode.Attributes["src"] != null)
            return imgNode.Attributes["src"].Value;
        
        return null;
    }
}
