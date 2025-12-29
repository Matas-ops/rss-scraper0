namespace BnsNewsRss.Models;

public record FeedItem(
    string Title,
    string Link,
    string Description,
    DateTime PubDate,
    string Guid,
    string Category,
    string Content,
    string? FeaturedImage
);