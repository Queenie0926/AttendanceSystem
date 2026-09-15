using System.IO;
using System.Text.Json;

namespace Attendance_System.Config
{
    /// <summary>
    /// Loads Supabase connection settings from appsettings.json (gitignored —
    /// copy appsettings.example.json and fill in real values).
    /// </summary>
    public static class AppConfig
    {
        public static string SupabaseUrl { get; private set; } = "";
        public static string SupabaseAnonKey { get; private set; } = "";

        public static void Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "appsettings.json not found. Copy appsettings.example.json to appsettings.json " +
                    "and fill in your Supabase project URL and anon key.", path);
            }

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);

            var supabase = doc.RootElement.GetProperty("Supabase");
            SupabaseUrl = supabase.GetProperty("Url").GetString() ?? "";
            SupabaseAnonKey = supabase.GetProperty("AnonKey").GetString() ?? "";

            if (string.IsNullOrWhiteSpace(SupabaseUrl) || SupabaseUrl.Contains("YOUR_PROJECT_REF") ||
                string.IsNullOrWhiteSpace(SupabaseAnonKey) || SupabaseAnonKey.Contains("YOUR_ANON_PUBLIC_KEY"))
            {
                throw new InvalidOperationException(
                    "appsettings.json still has placeholder values. Fill in your real Supabase URL and anon key.");
            }
        }
    }
}
