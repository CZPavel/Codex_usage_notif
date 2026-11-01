using CodexUsage.Authentication;
using CodexUsage.Errors;
using CodexUsage.Models;
using CodexUsage.Networking;
using CodexUsage.Parsing;

namespace CodexUsage.Services;

public sealed class CodexUsageService : IDisposable
{
    private readonly CodexCredentialStore _credentialStore;
    private readonly CodexUsageClient _client;
    private readonly bool _ownsClient;

    public CodexUsageService(
        CodexCredentialStore? credentialStore = null,
        CodexUsageClient? client = null)
    {
        _credentialStore = credentialStore ?? new CodexCredentialStore();
        _client = client ?? new CodexUsageClient();
        _ownsClient = client is null;
    }

    public string AuthFilePath => _credentialStore.AuthFilePath;

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        string json;

        try
        {
            json = await _client.GetUsageJsonAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (CodexUsageException exception) when (exception.Kind == CodexUsageErrorKind.Unauthorized)
        {
            // This local build leaves refresh-token rotation exclusively to Codex.
            // A process-local lock cannot coordinate writes with the running Codex app.
            throw new CodexUsageException(
                CodexUsageErrorKind.Unauthorized,
                "Open Codex to renew your login, then refresh Codex Usage. This build never updates your login.");
        }

        var email = JwtClaimsReader.TryReadEmail(credentials.IdToken);
        return CodexUsageParser.Parse(json, DateTimeOffset.UtcNow, email);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
