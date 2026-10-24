using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KultureCompass.Infrastructure.Persistence;

internal static class JsonValueConverters
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

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
