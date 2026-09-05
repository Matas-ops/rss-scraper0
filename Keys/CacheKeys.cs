namespace BnsNewsRss.Keys;

public static class CacheKeys
{
    public const string WordPressFeed = "wordpress_feed_xml";
    public static string ScrapedArticlesFeed = "scraped_articles_list";

    public static string Article(string guid)
    {
        return $"article::{guid}";
    }
}