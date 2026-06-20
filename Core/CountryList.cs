namespace IPWatcherPro.Core;

public record Country(string Code, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}

public static class CountryList
{
    public static readonly IReadOnlyList<Country> Countries = new List<Country>
    {
        new("US", "United States"),
        new("GB", "United Kingdom"),
        new("DE", "Germany"),
        new("FR", "France"),
        new("NL", "Netherlands"),
        new("JP", "Japan"),
        new("RU", "Russia"),
        new("UA", "Ukraine"),
        new("KZ", "Kazakhstan"),
        new("CN", "China"),
        new("IN", "India"),
        new("BR", "Brazil"),
        new("CA", "Canada"),
        new("AU", "Australia"),
        new("SG", "Singapore"),
        new("CH", "Switzerland"),
        new("SE", "Sweden"),
        new("FI", "Finland"),
        new("NO", "Norway"),
        new("IT", "Italy"),
        new("ES", "Spain")
    };

    public static Country? FindByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return Countries.FirstOrDefault(c =>
            c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}