namespace KultureCompass.Domain.Plans;

public sealed record GenerationOptions(bool IncludeCollaborations = true, bool IncludeCampaigns = true, int MaxCampaigns = 3)
{
    public static readonly GenerationOptions Default = new();
}
