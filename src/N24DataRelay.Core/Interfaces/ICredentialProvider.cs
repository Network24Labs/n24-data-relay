namespace N24DataRelay.Core.Interfaces;

/// <summary>Interface for credential retrieval (Linux-friendly; no DPAPI).</summary>
public interface ICredentialProvider
{
    /// <summary>Retrieve a credential by key (e.g. SSH key passphrase).</summary>
    string? GetCredential(string key);
    bool HasCredential(string key);
}
