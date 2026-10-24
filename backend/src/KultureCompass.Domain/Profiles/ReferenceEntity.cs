using KultureCompass.Domain.Common;

namespace KultureCompass.Domain.Profiles;

public sealed record ReferenceEntity
{
    public string Name { get; }
    public ReferenceKindHint KindHint { get; }

    private ReferenceEntity(string name, ReferenceKindHint kindHint)
    {
        Name = name;
        KindHint = kindHint;
    }

    public static ReferenceEntity Create(string name, ReferenceKindHint kindHint) =>
        new(Guard.RequiredLength(name, nameof(Name), 1, 100), kindHint);
}
