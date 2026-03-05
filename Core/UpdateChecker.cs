using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RcConnector.Core
{
    /// <summary>
    /// Checks GitHub releases for new versions.
    /// Downloads and launches installer for semi-automatic update.
    /// </summary>
    internal sealed class UpdateChecker
    {
        private const string RELEASES_URL = "https://api.github.com/repos/zvldz/RC-Connector/releases/latest";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "RC-Connector" } },
        };

        /// <summary>Latest version tag (e.g. "v0.3.0") after check.</summary>
        public string? LatestTag { get; private set; }

        /// <summary>Download URL of the Setup.exe asset.</summary>
        public string? DownloadUrl { get; private set; }

        /// <summary>Release page URL for fallback.</summary>
        public string? ReleaseUrl { get; private set; }

        /// <summary>
        /// Check if a newer version is available.
        /// Returns true if update found.
        /// </summary>
        public async Task<bool> CheckAsync()
        {
            try
            {
                using var response = await _http.GetAsync(RELEASES_URL);
                if (!response.IsSuccessStatusCode)
                    return false;
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                LatestTag = root.GetProperty("tag_name").GetString();
                ReleaseUrl = root.GetProperty("html_url").GetString();

                // Find Setup.exe asset
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            DownloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(LatestTag))
                    return false;

                var latestVersion = ParseVersion(LatestTag);
                var currentVersion = ParseVersion("v" + AppInfo.Version);

                return latestVersion > currentVersion;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Download installer to temp folder and launch it.
        /// Returns true if installer was started.
        /// </summary>
        public async Task<bool> DownloadAndLaunchAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
                return false;

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"RC-Connector-{LatestTag}-Setup.exe");
                using var response = await _http.GetAsync(DownloadUrl);
                response.EnsureSuccessStatusCode();

                using var fs = File.Create(tempPath);
                await response.Content.CopyToAsync(fs);
                fs.Close();

                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                return true;
            }
            catch
            {
                // Fallback: open release page in browser
                if (!string.IsNullOrEmpty(ReleaseUrl))
                    Process.Start(new ProcessStartInfo(ReleaseUrl) { UseShellExecute = true });
                return false;
            }
        }

        private static Version ParseVersion(string tag)
        {
            var clean = tag.TrimStart('v', 'V');
            return Version.TryParse(clean, out var v) ? v : new Version(0, 0, 0);
        }
    }
}
