using FluentValidation.Results;

namespace KultureCompass.Application.Common;

public static class ValidationExtensions
{
    /// <summary>Groups failures by camelCased property path ("ReferenceEntities[0].Name" →
    /// "referenceEntities[0].name") so error keys match the JSON field names clients sent.</summary>
    public static AppError ToAppError(this ValidationResult result) =>
        AppError.Validation(result.Errors
            .GroupBy(e => ToCamelPath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()));

    private static string ToCamelPath(string path) =>
        string.Join('.', path.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
