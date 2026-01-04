namespace BnsNewsRss.Models;

public record FeedItem(
    string Title,
    string Link,
    string Description,
    DateTime PubDate,
    string Guid,
    string BnsCategory,
    List<string> MappedCategories,
    string Content,
    string? FeaturedImage
);