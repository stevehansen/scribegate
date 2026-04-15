using System.Security.Cryptography;
using System.Text;
using Scribegate.Core.Entities;
using Scribegate.Data;

namespace Scribegate.Web.Api;

public class SignatureService
{
    private readonly ECDsa _key;
    private readonly string _publicKeyId;

    public SignatureService(IConfiguration configuration)
    {
        var dataPath = configuration["Scribegate:DataPath"] ?? "data";
        Directory.CreateDirectory(dataPath);

        var keyFile = Path.Combine(dataPath, ".signing-key.pem");
        var pubFile = Path.Combine(dataPath, ".signing-key.pub.pem");

        if (File.Exists(keyFile))
        {
            var pem = File.ReadAllText(keyFile);
            _key = ECDsa.Create();
            _key.ImportFromPem(pem);
        }
        else
        {
            _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privatePem = _key.ExportECPrivateKeyPem();
            var publicPem = _key.ExportSubjectPublicKeyInfoPem();
            File.WriteAllText(keyFile, privatePem);
            File.WriteAllText(pubFile, publicPem);
        }

        // Compute a fingerprint of the public key for identification
        var pubBytes = _key.ExportSubjectPublicKeyInfo();
        _publicKeyId = Convert.ToHexString(SHA256.HashData(pubBytes))[..16];
    }

    public RevisionSignature SignRevision(Revision revision)
    {
        var contentHash = ComputeHash(revision.Content);
        var hashBytes = Convert.FromHexString(contentHash);
        var signature = _key.SignHash(hashBytes);

        return new RevisionSignature
        {
            Id = Guid.CreateVersion7(),
            RevisionId = revision.Id,
            Algorithm = "ECDSA-P256",
            PublicKeyId = _publicKeyId,
            Signature = Convert.ToBase64String(signature),
            ContentHash = contentHash,
        };
    }

    public bool VerifyRevision(string content, RevisionSignature signature)
    {
        var contentHash = ComputeHash(content);
        if (contentHash != signature.ContentHash)
            return false;

        var hashBytes = Convert.FromHexString(contentHash);
        var sigBytes = Convert.FromBase64String(signature.Signature);

        return _key.VerifyHash(hashBytes, sigBytes);
    }

    public string GetPublicKeyPem() => _key.ExportSubjectPublicKeyInfoPem();
    public string GetPublicKeyId() => _publicKeyId;

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
