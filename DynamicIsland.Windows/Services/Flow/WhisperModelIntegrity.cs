using System.Security.Cryptography;

namespace DynamicIsland.Windows.Services.Flow;

/// <summary>Known SHA-256 digests of the pinned upstream Whisper.cpp base models.</summary>
public static class WhisperModelIntegrity
{
    // Upstream Git LFS pointers: https://huggingface.co/ggerganov/whisper.cpp/commit/80da2d8bfee42b0e836fc3a9890373e5defc00a6
    public static string? ExpectedSha256(string name) => name switch
    {
        "ggml-base.en.bin" => "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",
        "ggml-base.bin" => "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
        _ => null
    };

    public static bool Matches(byte[] digest, string? expected) => expected is not null
        && string.Equals(Convert.ToHexString(digest), expected, StringComparison.OrdinalIgnoreCase);

    public static bool CheckFile(string path, string modelName)
    {
        using var file = File.OpenRead(path);
        return Matches(SHA256.HashData(file), ExpectedSha256(modelName));
    }
}
