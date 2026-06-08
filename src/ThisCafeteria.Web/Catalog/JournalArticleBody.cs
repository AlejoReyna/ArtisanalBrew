namespace ThisCafeteria.Web.Catalog;

public sealed record JournalArticleBody(
    string Author,
    string PublishedDate,
    string Intro,
    IReadOnlyList<JournalArticleSection> Sections,
    string? ClosingNote = null);
