using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland.Windows.Services.Q;

public enum CodexRuntimeSource { Bundled, OfficialInstall, Path }

public sealed record CodexRuntimeInfo(
    string ExecutablePath,
    CodexRuntimeSource Source,
    string? Version = null,
    bool IsValidated = false);

internal sealed record CodexRuntimeManifest(string Version, IReadOnlyList<CodexRuntimeManifestFile> Files);
internal sealed record CodexRuntimeManifestFile(string Name, string Sha256);

public sealed class CodexRuntimeResolver
{
    public static readonly Version MinimumSupportedVersion = new(0, 151, 0);
    private static readonly string[] RequiredBundledFiles = ["codex.exe", "codex-code-mode-host.exe"];
    private readonly string _baseDirectory;
    private readonly string _localAppData;
    private readonly string _path;

    public CodexRuntimeResolver(string? baseDirectory = null, string? localAppData = null, string? path = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _localAppData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _path = path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    }

    public CodexRuntimeInfo Resolve()
    {
        var bundled = TryBundled();
        if (bundled is not null) return bundled;

        var official = OfficialCandidates()
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
        if (official is not null) return new CodexRuntimeInfo(official, CodexRuntimeSource.OfficialInstall);

        var fromPath = _path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "codex.exe"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null) return new CodexRuntimeInfo(fromPath, CodexRuntimeSource.Path);

        throw new CodexAppServerException(CodexFailureKind.RuntimeMissing,
            "Codex is not available. Use the Codex-bundled test build or install the official Codex app.");
    }

    private CodexRuntimeInfo? TryBundled()
    {
        var directory = Path.Combine(_baseDirectory, "codex");
        var executable = Path.Combine(directory, "codex.exe");
        var manifestPath = Path.Combine(directory, "codex-runtime.json");
        if (!File.Exists(executable) || !File.Exists(manifestPath)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<CodexRuntimeManifest>(File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !CodexRuntimeVersion.TryParse(manifest.Version, out var version) || !version.IsAtLeast(MinimumSupportedVersion))
                throw new InvalidDataException("The bundled Codex version is not supported.");
            if (manifest.Files is null || manifest.Files.Count != RequiredBundledFiles.Length ||
                RequiredBundledFiles.Any(required => !manifest.Files.Any(file => string.Equals(file.Name, required, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("The bundled Codex manifest must verify every required executable.");
            foreach (var fileName in RequiredBundledFiles)
            {
                var file = manifest.Files.Single(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"Bundled Codex file '{fileName}' has an invalid checksum.");
                var path = Path.Combine(directory, fileName);
                if (!File.Exists(path)) throw new InvalidDataException($"Bundled Codex file '{fileName}' is missing.");
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bundled Codex file '{fileName}' failed verification.");
            }
            return new CodexRuntimeInfo(executable, CodexRuntimeSource.Bundled, manifest.Version, true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException)
        {
            throw new CodexAppServerException(CodexFailureKind.RuntimeInvalid,
                "The bundled Codex runtime is incomplete or failed its integrity check.", innerException: ex);
        }
    }

    private IEnumerable<string> OfficialCandidates()
    {
        yield return Path.Combine(_localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        var versioned = Path.Combine(_localAppData, "OpenAI", "Codex", "bin");
        if (!Directory.Exists(versioned)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(versioned))
            yield return Path.Combine(directory, "codex.exe");
    }
}
