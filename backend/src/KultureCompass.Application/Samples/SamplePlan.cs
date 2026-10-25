using System.Reflection;

namespace KultureCompass.Application.Samples;

/// <summary>GET /sample-plan: a hand-written, clearly labelled synthetic plan (isMock=true, isSample=true, every entity
/// "Synthetic …") in the GrowthPlanDto shape. Public and cacheable by the service worker; never real Qloo data.</summary>
public static class SamplePlan
{
    public static string Json { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(SamplePlan).GetTypeInfo().Assembly.GetManifestResourceStream("Samples.sample-plan.json")
            ?? throw new InvalidOperationException("Missing embedded sample plan.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
