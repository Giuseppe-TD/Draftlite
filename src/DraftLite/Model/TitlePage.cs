using System;

namespace DraftLite.Model;

/// <summary>Frontespizio: gli unici campi che servono davvero.</summary>
public sealed class TitlePage
{
    public string Title { get; set; } = string.Empty;
    public string Credit { get; set; } = "scritto da";
    public string Author { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string DraftDate { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Copyright { get; set; } = string.Empty;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) &&
        string.IsNullOrWhiteSpace(Author) &&
        string.IsNullOrWhiteSpace(Source) &&
        string.IsNullOrWhiteSpace(DraftDate) &&
        string.IsNullOrWhiteSpace(Contact) &&
        string.IsNullOrWhiteSpace(Copyright);

    public TitlePage Clone() => new TitlePage
    {
        Title = Title,
        Credit = Credit,
        Author = Author,
        Source = Source,
        DraftDate = DraftDate,
        Contact = Contact,
        Copyright = Copyright
    };
}
