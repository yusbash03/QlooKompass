namespace KultureCompass.Domain.Plans;

public sealed record UnlinkedMention(string Name, string Note)
{
    public const string DefaultNote = "AI suggestion — not retrieved from Qloo";
}
