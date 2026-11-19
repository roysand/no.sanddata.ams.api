using Domain.Common;

namespace Application.Common.Interfaces.Security;

/// <summary>Encrypts a secret for storage, bound to the id of the row it belongs to.</summary>
public interface ISecretProtector
{
    /// <summary>False when the server has no (valid) encryption key; <see cref="Protect"/> must not be called then.</summary>
    bool IsConfigured { get; }

    string Protect(string plaintext, Guid associatedId);

    /// <summary>Fails with <c>WaterHeater.KeyUnreadable</c> when the value cannot be decrypted; never throws.</summary>
    Result<string> Unprotect(string protectedValue, Guid associatedId);
}
