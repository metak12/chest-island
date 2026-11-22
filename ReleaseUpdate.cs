using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class ReleaseUpdate
{
    private const string Repo = "metak12/chest-island";
    private const string Name = "Chest-Island-Setup.exe";
    public const string VersionText = "1.1.2";
    private readonly Uri url;
    private readonly string digest;
    public readonly Version Version;

    private ReleaseUpdate(Version version, Uri url, string digest)
    {
        Version = version;
        this.url = url;
        this.digest = digest;
    }

    private sealed class GitHubClient : WebClient
    {
        public GitHubClient()
        {
            // The standalone .NET Framework executable can otherwise select legacy TLS.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Headers[HttpRequestHeader.UserAgent] = "ChestIsland/" + VersionText;
        }
        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = 15000;
            var http = request as HttpWebRequest;
            if (http != null) http.ReadWriteTimeout = 15000;
            return request;
        }
    }

    public static Task<ReleaseUpdate> Check()
    {
        return Task.Run(() =>
        {
            using (var client = new GitHubClient())
            {
                var payload = client.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                var release = new JavaScriptSerializer().Deserialize<Release>(payload);
                var tag = release.tag_name ?? "";
                Version latest;
                if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") ||
                    !Version.TryParse(tag.Substring(1), out latest) || latest <= new Version(VersionText)) return null;
                foreach (var asset in release.assets ?? new Asset[0])
                {
                    var expected = "https://github.com/" + Repo + "/releases/download/" + tag + "/" + Name;
                    if (asset.name == Name && asset.browser_download_url == expected &&
                        Regex.IsMatch(asset.digest ?? "", @"^sha256:[0-9a-fA-F]{64}$"))
                        return new ReleaseUpdate(latest, new Uri(expected), asset.digest.Substring(7));
                }
                throw new InvalidDataException("У релиза отсутствует установщик с SHA-256.");
            }
        });
    }

    public string Download()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ChestIsland-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Name);
        try
        {
            using (var client = new GitHubClient()) client.DownloadFile(url, path);
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(hash.ComputeHash(file)).Replace("-", ""), digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 установщика не совпадает.");
            return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    private sealed class Release
    {
        public string tag_name { get; set; }
        public Asset[] assets { get; set; }
    }

    private sealed class Asset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public string digest { get; set; }
    }
}
