namespace Infrastructure.Logging;

public static class FailureReasons
{
    // Textual tokens (preferred in logs)
    public const string NoSuchUser = "NoSuchUser";
    public const string BadCredentials = "BadCredentials";
    public const string AccountLocked = "AccountLocked";
    public const string AccountDisabled = "AccountDisabled";
    public const string TwoFactorRequired = "TwoFactorRequired";
    public const string PasswordTooWeak = "PasswordTooWeak";
    public const string EmailAlreadyExists = "EmailAlreadyExists";
    public const string InvalidToken = "InvalidToken";
    public const string ExpiredToken = "ExpiredToken";
    public const string InvalidRefreshToken = "InvalidRefreshToken";
    public const string RefreshTokenExpired = "RefreshTokenExpired";
    public const string RateLimited = "RateLimited";
    public const string SystemError = "SystemError";

    // Numeric codes (optional)
    public const int NoSuchUserCode = 1001;
    public const int BadCredentialsCode = 1002;
    public const int AccountLockedCode = 1003;
    public const int AccountDisabledCode = 1004;
    public const int TwoFactorRequiredCode = 1005;
    public const int PasswordTooWeakCode = 2001;
    public const int EmailAlreadyExistsCode = 2002;
    public const int InvalidTokenCode = 3001;
    public const int ExpiredTokenCode = 3002;
    public const int InvalidRefreshTokenCode = 3003;
    public const int RefreshTokenExpiredCode = 3004;
    public const int RateLimitedCode = 4001;
    public const int SystemErrorCode = 5000;
}

