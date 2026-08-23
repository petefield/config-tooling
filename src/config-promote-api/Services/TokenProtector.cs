using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace config_promote_api.Services;

internal sealed class TokenProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public TokenProtector(GitHubAppOptions options)
    {
        _key = options.SessionKey;
    }

    public string Protect<T>(T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);
        return Convert.ToBase64String(combined);
    }

    public T Unprotect<T>(string token)
    {
        try
        {
            var combined = Convert.FromBase64String(token);
            var nonceLength = AesGcm.NonceByteSizes.MaxSize;
            var tagLength = AesGcm.TagByteSizes.MaxSize;

            if (combined.Length <= nonceLength + tagLength)
            {
                throw new AuthenticationRequiredException("The GitHub sign-in session is invalid. Sign in again and retry.");
            }

            var nonce = combined[..nonceLength];
            var tag = combined[nonceLength..(nonceLength + tagLength)];
            var ciphertext = combined[(nonceLength + tagLength)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, tagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return JsonSerializer.Deserialize<T>(plaintext, JsonOptions)
                ?? throw new AuthenticationRequiredException("The GitHub sign-in session is empty. Sign in again and retry.");
        }
        catch (FormatException exception)
        {
            throw new AuthenticationRequiredException("The GitHub sign-in session is malformed. Sign in again and retry.", exception);
        }
        catch (CryptographicException exception)
        {
            throw new AuthenticationRequiredException("The GitHub sign-in session could not be verified. Sign in again and retry.", exception);
        }
        catch (JsonException exception)
        {
            throw new AuthenticationRequiredException("The GitHub sign-in session payload is invalid. Sign in again and retry.", exception);
        }
    }
}
