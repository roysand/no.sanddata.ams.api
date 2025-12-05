using System.Net.Mail;

namespace Domain.Common.ValueObjects;

public sealed class EmailAddress : ValueObject
{
    public string Value { get; private set; }

    // Private constructor - prevents direct instantiation
    private EmailAddress(string value)
    {
        Value = value;
    }

    // Factory method returning Result<EmailAddress>
    public static Result<EmailAddress> Create(string? value)
    {
        // Validation: null or whitespace
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<EmailAddress>(
                Error.Validation(
                    "EmailAddress.Empty",
                    "Email address cannot be empty."));
        }

        // Normalize: trim and lowercase
        var normalizedValue = value.Trim().ToLowerInvariant();

        // Validation: format
        if (!IsValidEmail(normalizedValue))
        {
            return Result.Failure<EmailAddress>(
                Error.Validation(
                    "EmailAddress.InvalidFormat",
                    "Email address format is invalid."));
        }

        // Success: create instance
        return Result.Success(new EmailAddress(normalizedValue));
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var mailAddress = new MailAddress(email);
            return mailAddress.Address == email;
        }
        catch
        {
            return false;
        }
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
