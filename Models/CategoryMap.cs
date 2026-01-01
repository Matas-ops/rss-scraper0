namespace BnsNewsRss.Models;

public class CategoryMap
{
    public static readonly Dictionary<string[], string> Bns = new()
    {
        //compared as starts with
        //from -> to
        {["ES", "Krašto apsauga", "Politika", "Savivalda", "Švietimas", "Tarptaut.", "Teisėsauga"], "Aktualijos" },
        {["Energetika", "ITT", "Maisto pramonė", "NT", "Pramonė", "Prekyba", "Socialinė sauga", "Transportas", "Turizmas", "Žiniasklaida", "Verslas", "Žemės ūkis"], "Verslas" },
        {["Ekologija", "Gamta"], "Margumynai" },
        //{[], "Nuomonės" },
        {["Kultūra", "Laisvalaikis"], "Pramogos" },
        {["Sportas"], "Sportas" },
        {["Medicina", "Sveikata"], "Sveikata" },
        {["ITT"], "Technologijos" },
        {["Energetika", "Maisto pramonė", "NT", "Pramonė", "Prekyba", "Socialinė sauga", "Turizmas", "Verslas", "Žemės ūkis", "Žiniasklaida"], "Ekonomika" },
        {["Transportas"], "Transportas" },
    };
}