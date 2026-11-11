namespace BnsNewsRss.Constants;

public static class Configuration
{
    public const string HostUrl = "https://rss.geras.page";
    public const int FetchIntervalHours = 2;

    /// <summary>WordPress (RSS Category Importer plugin) endpoint that is told to import the refreshed feeds.</summary>
    public const string WordPressImportUrl = "https://ziniu.lt/wp-json/rss-importer/v1/import";

    /// <summary>Environment variable holding the plugin's webhook token (from .env locally, defined in production).</summary>
    public const string WordPressImportTokenVariable = "RSS_IMPORTER_TOKEN";

    /// <summary>Optional environment variable overriding <see cref="WordPressImportUrl" /> (e.g. for a staging site).</summary>
    public const string WordPressImportUrlVariable = "RSS_IMPORTER_URL";
}