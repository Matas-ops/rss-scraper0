namespace BnsNewsRss.Keys;

public static class CacheKeys
{
    public const string WordPressFeed = "wordpress_feed_xml";

    public static string Article(string guid) => $"article::{guid}";
}