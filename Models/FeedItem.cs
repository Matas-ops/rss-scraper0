namespace BnsNewsRss.Models;

public record FeedItem(
    string Title,
    string Link,
    string Description,
    DateTime PubDate,
    string Guid,
    //TODO make this a list? Retain parse of 1 category from xml, but for string builder it would be nice to have multiple since some articles have multiple categories
    //Right now for string builder this is passed: "category1, category2, category3"
    string Category,
    string Content,
    string? FeaturedImage
);