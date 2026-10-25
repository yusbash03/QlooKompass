using FluentValidation;

namespace KultureCompass.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(r => r.Password).NotEmpty().Length(10, 128)
            .Must(HaveThreeCharacterClasses)
            .WithMessage("Password must contain at least 3 of: lowercase, uppercase, digit, symbol.");
        RuleFor(r => r.DisplayName).MaximumLength(80);
    }

    /// <summary>02-security §1 policy ("3 of 4 character classes") — Identity's Require* options can only make each
    /// class mandatory, so this rule lives here and Identity only enforces the length.</summary>
    internal static bool HaveThreeCharacterClasses(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        return classes >= 3;
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}
