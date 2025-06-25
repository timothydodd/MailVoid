using System.Security.Cryptography;
using System.Text;

namespace MailVoidSmtpServer.Utils;

/// <summary>
/// Utility class for generating password hashes for SMTP authentication.
/// </summary>
public static class PasswordHasher
{
    /// <summary>
    /// Generates a SHA256 hash of a password.
    /// </summary>
    /// <param name="password">The password to hash</param>
    /// <returns>Base64 encoded hash</returns>
    public static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Main method to run from command line to generate password hashes.
    /// Usage: dotnet run --project MailVoidSmtpServer -- hash-password yourpassword
    /// </summary>
    public static void GeneratePasswordHash(string[] args)
    {
        if (args.Length != 2 || args[0] != "hash-password")
        {
            Console.WriteLine("Usage: dotnet run -- hash-password <password>");
            return;
        }

        var password = args[1];
        var hash = HashPassword(password);
        
        Console.WriteLine($"Password: {password}");
        Console.WriteLine($"SHA256 Hash: {hash}");
        Console.WriteLine($"\nAdd this to your appsettings.json:");
        Console.WriteLine($"\"username\": \"{hash}\"");
    }
}