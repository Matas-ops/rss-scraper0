namespace BnsNewsRss.Mappers;

public class CategoryMapper
{
    /// <summary>
    /// https://sc.bns.lt/rss
    /// </summary>
    private static readonly Dictionary<string[], string> BnsMap = new()
    {
        //from -> to
        {["Europos Sąjunga", "Krašto apsauga", "Politika", "Savivalda, regionai", "Švietimas", "Tarptautiniai santykiai", "Teisėsauga"], "Aktualijos" },
        {["Energetika", "IT&T", "Maisto pramonė", "NT, statyba", "Pramonė, gamyba", "Prekyba", "Socialinė sauga", "Transportas", "Turizmas", "Žiniasklaida", "Žemės ūkis", "Finansai"], "Verslas" },
        {["Ekologija", "Gamta", "Kiti pranešimai"], "Margumynai" },
        //{[], "Nuomonės" },
        {["Kultūra", "Laisvalaikis"], "Pramogos" },
        {["Sportas"], "Sportas" },
        {["Medicina, farmacija", "Sveikata"], "Sveikata" },
        {["IT&T"], "Technologijos" },
        {["Energetika", "Maisto pramonė", "NT, statyba", "Pramonė, gamyba", "Prekyba", "Socialinė sauga", "Turizmas", "Žemės ūkis", "Žiniasklaida", "Ekonomika", "Finansai"], "Ekonomika" },
        {["Transportas"], "Transportas" },
    };
    
    public static readonly List<string> AllCategories = BnsMap.Values.ToList();
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="bnsTopic"></param>
    /// <returns>Categories used aggregated into string, separated by commas</returns>
    public static List<string> MapBnsTopicToCategory(string bnsTopic)
    {
        List<string> mappedCategories = [];
        
        foreach (var entry in BnsMap)
        {
            if (entry.Key.Contains(bnsTopic))
            {
                mappedCategories.Add(entry.Value);
            }
        }

        if (!mappedCategories.Any())
        {
            Console.Error.WriteLine($"{bnsTopic} not mapped to any category");
        }

        return mappedCategories;
    }
}