using System.Text.Json;
using System.Text.Json.Serialization;
using KultureCompass.Domain.Profiles;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KultureCompass.Infrastructure.Persistence;

internal static class JsonValueConverters
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new ReferenceEntityJsonConverter() },
    };

    /// <summary>ReferenceEntity only exposes a private ctor + <c>Create</c> factory (keeps Domain free of serializer
    /// attributes), so jsonb reads go through the factory and re-run its invariants.</summary>
    private sealed class ReferenceEntityJsonConverter : JsonConverter<ReferenceEntity>
    {
        private sealed record Wire(string Name, ReferenceKindHint KindHint);

        public override ReferenceEntity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var wire = JsonSerializer.Deserialize<Wire>(ref reader, options)
                ?? throw new JsonException("ReferenceEntity was null.");
            return ReferenceEntity.Create(wire.Name, wire.KindHint);
        }

        public override void Write(Utf8JsonWriter writer, ReferenceEntity value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new Wire(value.Name, value.KindHint), options);
    }

    public static ValueConverter<T, string> For<T>() => new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<T>(v, Options)!);

    public static ValueConverter<T?, string?> ForNullableClass<T>() where T : class => new(
        v => v == null ? null : JsonSerializer.Serialize(v, Options),
        v => v == null ? null : JsonSerializer.Deserialize<T>(v, Options));

    public static ValueConverter<T?, string?> ForNullableStruct<T>() where T : struct => new(
        v => v == null ? null : JsonSerializer.Serialize(v, Options),
        v => v == null ? null : JsonSerializer.Deserialize<T>(v, Options));

    public static ValueComparer<IReadOnlyList<T>> ReadOnlyListComparer<T>() => new(
        (a, b) => (a ?? new List<T>()).SequenceEqual(b ?? new List<T>()),
        v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
        v => (IReadOnlyList<T>)v.ToList());
}
