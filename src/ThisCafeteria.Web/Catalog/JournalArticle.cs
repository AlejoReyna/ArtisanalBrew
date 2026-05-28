namespace ThisCafeteria.Web.Catalog;

public sealed record JournalArticle(
    string Kicker,
    string Title,
    string Summary,
    string CategoryLabel,
    string ReadTime,
    string ImageKey,
    string ImageAriaLabel,
    bool IsFeature = false,
    bool IsDark = false);
