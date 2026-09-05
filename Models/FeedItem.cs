namespace BnsNewsRss.Models;

public record FeedItem(
    string Title,
    string Link,
    string Description,
    DateTime PubDate,
    string Guid,
    string BnsCategory,
    List<string> MappedCategories
)
{
    public string? FeaturedImage { get; set; }
    public string Content { get; set; } = string.Empty;
}