using System.Text.RegularExpressions;

namespace MailVoidApi.Common;

public static class ConfigurationDumper
{
    public static void DumpIfEnabled(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("DumpConfiguration"))
        {
            return;
        }

        Console.WriteLine("===== Configuration Dump =====");
        foreach (var kvp in configuration.AsEnumerable().OrderBy(k => k.Key))
        {
            var value = kvp.Value;
            if (!string.IsNullOrEmpty(value))
            {
                bool isConnString = kvp.Key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase) ||
                                    kvp.Key.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase);
                if (isConnString)
                {
                    value = Regex.Replace(
                        value,
                        @"(Password|Pwd)\s*=\s*[^;]*",
                        "$1=***",
                        RegexOptions.IgnoreCase);
                }
                else if (kvp.Key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                         kvp.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                         kvp.Key.Contains("Key", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Length <= 8 ? "***" : value.Substring(0, 4) + "***" + value.Substring(value.Length - 4);
                }
            }
            Console.WriteLine($"  {kvp.Key} = {value}");
        }
        Console.WriteLine("===== End Configuration Dump =====");
    }
}
