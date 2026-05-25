namespace Features.Auth.Logging;

// This file forwards to the shared Infrastructure FailureReasons to avoid
// duplicate definitions. Prefer using Infrastructure.Logging.FailureReasons
// directly; this file exists for backwards compatibility.
internal static class FailureReasons
{
    public const string NoSuchUser = Infrastructure.Logging.FailureReasons.NoSuchUser;
    public const string BadCredentials = Infrastructure.Logging.FailureReasons.BadCredentials;
    public const string AccountLocked = Infrastructure.Logging.FailureReasons.AccountLocked;
    public const string AccountDisabled = Infrastructure.Logging.FailureReasons.AccountDisabled;
    public const string TwoFactorRequired = Infrastructure.Logging.FailureReasons.TwoFactorRequired;
    public const string PasswordTooWeak = Infrastructure.Logging.FailureReasons.PasswordTooWeak;
    public const string EmailAlreadyExists = Infrastructure.Logging.FailureReasons.EmailAlreadyExists;
    public const string InvalidToken = Infrastructure.Logging.FailureReasons.InvalidToken;
    public const string ExpiredToken = Infrastructure.Logging.FailureReasons.ExpiredToken;
    public const string InvalidRefreshToken = Infrastructure.Logging.FailureReasons.InvalidRefreshToken;
    public const string RefreshTokenExpired = Infrastructure.Logging.FailureReasons.RefreshTokenExpired;
    public const string RateLimited = Infrastructure.Logging.FailureReasons.RateLimited;
    public const string SystemError = Infrastructure.Logging.FailureReasons.SystemError;

    public const int NoSuchUserCode = Infrastructure.Logging.FailureReasons.NoSuchUserCode;
    public const int BadCredentialsCode = Infrastructure.Logging.FailureReasons.BadCredentialsCode;
    public const int AccountLockedCode = Infrastructure.Logging.FailureReasons.AccountLockedCode;
    public const int AccountDisabledCode = Infrastructure.Logging.FailureReasons.AccountDisabledCode;
    public const int TwoFactorRequiredCode = Infrastructure.Logging.FailureReasons.TwoFactorRequiredCode;
    public const int PasswordTooWeakCode = Infrastructure.Logging.FailureReasons.PasswordTooWeakCode;
    public const int EmailAlreadyExistsCode = Infrastructure.Logging.FailureReasons.EmailAlreadyExistsCode;
    public const int InvalidTokenCode = Infrastructure.Logging.FailureReasons.InvalidTokenCode;
    public const int ExpiredTokenCode = Infrastructure.Logging.FailureReasons.ExpiredTokenCode;
    public const int InvalidRefreshTokenCode = Infrastructure.Logging.FailureReasons.InvalidRefreshTokenCode;
    public const int RefreshTokenExpiredCode = Infrastructure.Logging.FailureReasons.RefreshTokenExpiredCode;
    public const int RateLimitedCode = Infrastructure.Logging.FailureReasons.RateLimitedCode;
    public const int SystemErrorCode = Infrastructure.Logging.FailureReasons.SystemErrorCode;
}

