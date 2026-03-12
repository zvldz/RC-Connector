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

        /// <summary>Called during download with (bytesReceived, totalBytes). totalBytes = -1 if unknown.</summary>
        public event Action<long, long>? DownloadProgress;

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
        /// Reports progress via DownloadProgress event.
        /// Returns true if installer was started.
        /// </summary>
        public async Task<bool> DownloadAndLaunchAsync()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
                return false;

            try
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"RC-Connector-{LatestTag}-Setup.exe");

                // Stream download with progress reporting, 3 min timeout
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                long bytesRead = 0;
                int lastPercent = -1;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fs = File.Create(tempPath);
                var buffer = new byte[81920]; // 80 KB chunks
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read, cts.Token);
                    bytesRead += read;

                    // Report progress every ~10%
                    if (totalBytes > 0)
                    {
                        int percent = (int)(bytesRead * 100 / totalBytes);
                        int step = percent / 10 * 10; // round to 10%
                        if (step > lastPercent)
                        {
                            lastPercent = step;
                            DownloadProgress?.Invoke(bytesRead, totalBytes);
                        }
                    }
                }

                fs.Close();

                // Launch installer — separate try/catch so user cancel doesn't trigger browser fallback
                try
                {
                    Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                }
                catch
                {
                    // User declined SmartScreen or similar — not an error, just skip
                    return false;
                }
                return true;
            }
            catch
            {
                // Download failed — fallback: open release page in browser
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
