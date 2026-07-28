using System.Net;
using System.Net.Http.Headers;

namespace BaChenAiLauncher;

internal enum ExternalAuthorizationStatus
{
    Authorized,
    InvalidCredential,
    AccessNotGranted,
    ProbeNotFound,
    NetworkError,
    UnsupportedProvider
}

internal sealed record ExternalAuthorizationResult(ExternalAuthorizationStatus Status, string Message)
{
    public bool IsAuthorized => Status == ExternalAuthorizationStatus.Authorized;
}

internal sealed class ExternalModelAuthorizationService(HttpClient httpClient)
{
    public const string HuggingFaceCredentialTarget = "BaChenAILauncher/HuggingFace";

    public async Task<ExternalAuthorizationResult> VerifyAsync(PluginPackageManifest manifest, string token, CancellationToken cancellationToken = default)
    {
        if (!manifest.RequiresExternalAuthorization)
        {
            return new ExternalAuthorizationResult(ExternalAuthorizationStatus.Authorized, "External authorization is not required.");
        }
        if (!manifest.ModelProvider.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
        {
            return new ExternalAuthorizationResult(ExternalAuthorizationStatus.UnsupportedProvider, $"Unsupported authorization provider: {manifest.ModelProvider}");
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ExternalAuthorizationResult(ExternalAuthorizationStatus.InvalidCredential, "A Hugging Face read token is required.");
        }
        try
        {
            using var whoAmI = new HttpRequestMessage(HttpMethod.Get, "https://huggingface.co/api/whoami-v2");
            whoAmI.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            using var identityResponse = await httpClient.SendAsync(whoAmI, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (identityResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new ExternalAuthorizationResult(ExternalAuthorizationStatus.InvalidCredential, "The Hugging Face token is invalid or expired.");
            }
            identityResponse.EnsureSuccessStatusCode();

            var probePath = string.IsNullOrWhiteSpace(manifest.AuthorizationProbePath) ? "README.md" : manifest.AuthorizationProbePath.TrimStart('/');
            var probeUrl = $"https://huggingface.co/{manifest.ModelId}/resolve/main/{probePath}";
            using var probe = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            using var probeResponse = await httpClient.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return probeResponse.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new ExternalAuthorizationResult(ExternalAuthorizationStatus.InvalidCredential, "The Hugging Face token was rejected."),
                HttpStatusCode.Forbidden => new ExternalAuthorizationResult(ExternalAuthorizationStatus.AccessNotGranted, "The account has not been granted access to this gated model. Complete the upstream authorization first."),
                HttpStatusCode.NotFound => new ExternalAuthorizationResult(ExternalAuthorizationStatus.ProbeNotFound, "The authorization probe file was not found. Check modelId and authorizationProbePath."),
                _ when probeResponse.IsSuccessStatusCode => new ExternalAuthorizationResult(ExternalAuthorizationStatus.Authorized, "Hugging Face identity and gated model access were verified."),
                _ => new ExternalAuthorizationResult(ExternalAuthorizationStatus.NetworkError, $"Hugging Face returned HTTP {(int)probeResponse.StatusCode}.")
            };
        }
        catch (HttpRequestException ex)
        {
            return new ExternalAuthorizationResult(ExternalAuthorizationStatus.NetworkError, $"Could not reach Hugging Face: {ex.Message}");
        }
    }
}
