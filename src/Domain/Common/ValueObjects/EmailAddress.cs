namespace Domain.Common.ValueObjects;

using Domain.Common.ValueObjects;

public sealed class EmailAddress : ValueObject
{
    public string Value { get; }

    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsValidEmail(value))
            throw new ArgumentException("Invalid email address.", nameof(value));
        Value = value;
    }

    private static bool IsValidEmail(string email)
    {
        // Basic validation, consider Regex for production
        return email.Contains("@");
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
