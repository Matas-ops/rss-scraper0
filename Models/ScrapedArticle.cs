namespace BnsNewsRss.Models;

public record ScrapedArticle(
    string Content,
    string? FeaturedImage
);