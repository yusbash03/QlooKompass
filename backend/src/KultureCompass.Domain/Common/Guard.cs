namespace KultureCompass.Domain.Common;

internal static class Guard
{
    public static string RequiredLength(string? value, string fieldName, int minLength, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{fieldName} is required.");
        if (value.Length < minLength || value.Length > maxLength)
            throw new DomainException($"{fieldName} must be between {minLength} and {maxLength} characters.");
        return value;
    }

    public static string? OptionalMaxLength(string? value, string fieldName, int maxLength)
    {
        if (value is null) return null;
        if (value.Length > maxLength)
            throw new DomainException($"{fieldName} must be at most {maxLength} characters.");
        return value;
    }

    public static void MaxItems<T>(IReadOnlyCollection<T> items, string fieldName, int maxCount)
    {
        if (items.Count > maxCount)
            throw new DomainException($"{fieldName} must contain at most {maxCount} items.");
    }

    public static void Range(int value, string fieldName, int min, int max)
    {
        if (value < min || value > max)
            throw new DomainException($"{fieldName} must be between {min} and {max}.");
    }
}
