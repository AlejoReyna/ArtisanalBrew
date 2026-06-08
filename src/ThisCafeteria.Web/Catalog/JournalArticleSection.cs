namespace ThisCafeteria.Web.Catalog;

public sealed record JournalArticleSection(
    string? Eyebrow,
    string? Heading,
    string Body,
    string? PullQuote = null,
    bool IsAside = false);
