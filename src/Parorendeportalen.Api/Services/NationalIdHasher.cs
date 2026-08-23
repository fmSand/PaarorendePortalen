using System.Security.Cryptography;
using System.Text;

namespace Parorendeportalen.Api.Services;

public sealed class NationalIdHasher(string pepper)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(pepper);

    public string Hash(string nationalId) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(nationalId)));
}
