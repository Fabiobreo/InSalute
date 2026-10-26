using System.Security.Cryptography;

namespace InSalute.Core.Dispatching;

public static class PasswordGenerator
{
    // Senza caratteri ambigui: 0 O o 1 l I.
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
    public const int Length = 12;

    public static string Generate()
    {
        while (true)
        {
            var password = RandomNumberGenerator.GetString(Alphabet, Length);
            if (password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit))
            {
                return password;
            }
        }
    }
}
