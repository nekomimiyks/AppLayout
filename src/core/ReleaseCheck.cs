using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AppLayout
{
    internal static class ReleaseCheck
    {
        const string DownloadBase = "https://github.com/nekomimiyks/AppLayout/releases/download/";
        const string InstallerName = "AppLayout-Setup.exe";
        const int InstallerLimit = 50 * 1024 * 1024;
        const string Endpoint = "https://api.github.com/repos/nekomimiyks/AppLayout/releases/latest";
        internal static readonly Version InstalledVersion = ReadInstalledVersion();

        [DataContract]
        sealed class Release
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public Asset[] Assets { get; set; }
        }

        [DataContract]
        sealed class Asset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "state")] public string State { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
        }

        internal sealed class Update
        {
            internal readonly Version Version;
            internal readonly string Hash;
            internal Update(Version version, string hash) { Version = version; Hash = hash; }
        }

        static Version ReadInstalledVersion()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppLayout.ReleaseVersion");
            if (stream == null) return null;
            using (var reader = new StreamReader(stream))
                return ReadVersion(reader.ReadToEnd());
        }

        static Version ReadVersion(string value)
        {
            Version version;
            return Version.TryParse(value, out version) && version.Build >= 0 && version.Revision < 0 ? version : null;
        }

        internal static Update FindNewerRelease(Version installed, Stream json)
        {
            var release = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(json);
            if (installed == null || release == null || release.Draft || release.Prerelease ||
                string.IsNullOrEmpty(release.Tag) || release.Tag[0] != 'v') return null;
            var latest = ReadVersion(release.Tag.Substring(1));
            if (latest == null || latest <= installed || release.Tag != "v" + latest || release.Assets == null) return null;
            foreach (var asset in release.Assets)
                if (asset != null && asset.Name == InstallerName && asset.State == "uploaded" &&
                    asset.Digest != null && Regex.IsMatch(asset.Digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                    return new Update(latest, asset.Digest.Substring(7));
            return null;
        }

        static HttpWebRequest Request(string url, int timeout)
        {
            // このビルドの .NET Framework 互換設定は既定で古いTLSを選ぶ。
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "AppLayout/" + InstalledVersion;
            request.AllowAutoRedirect = false;
            request.Timeout = request.ReadWriteTimeout = timeout;
            return request;
        }

        internal static Update CheckLatest()
        {
            var request = Request(Endpoint, 10000);
            request.Accept = "application/vnd.github+json";
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK) return null;
                using (var json = response.GetResponseStream())
                    return FindNewerRelease(InstalledVersion, json);
            }
        }

        internal static string Download(Update update)
        {
            string folder = Path.Combine(Path.GetTempPath(), "AppLayout-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, InstallerName);
            try
            {
                string url = DownloadBase + "v" + update.Version + "/" + InstallerName;
                using (var response = DownloadResponse(new Uri(url)))
                using (var source = response.GetResponseStream())
                using (var output = File.Open(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    var timer = Stopwatch.StartNew();
                    var buffer = new byte[65536];
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + count > InstallerLimit || timer.Elapsed.TotalMinutes > 2)
                            throw new IOException("AppLayout installer download limit exceeded.");
                        output.Write(buffer, 0, count);
                    }
                    output.Position = 0;
                    using (var sha = SHA256.Create())
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(output)).Replace("-", "");
                        if (!string.Equals(hash, update.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("AppLayout installer checksum mismatch.");
                    }
                }
                return path;
            }
            catch
            {
                RemoveDownload(path);
                throw;
            }
        }

        static HttpWebResponse DownloadResponse(Uri address)
        {
            for (int i = 0; i < 4; i++)
            {
                if (address.Scheme != Uri.UriSchemeHttps || !address.IsDefaultPort || address.UserInfo.Length != 0 ||
                    (address.Host != "github.com" && address.Host != "release-assets.githubusercontent.com" &&
                        address.Host != "objects.githubusercontent.com"))
                    throw new IOException("Unexpected AppLayout installer URL.");
                var response = (HttpWebResponse)Request(address.AbsoluteUri, 20000).GetResponse();
                if (response.StatusCode == HttpStatusCode.OK) return response;
                using (response)
                {
                    string location = response.Headers["Location"];
                    if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 ||
                        !Uri.TryCreate(address, location, out address)) break;
                }
            }
            throw new IOException("AppLayout installer download failed.");
        }

        internal static void RemoveDownload(string path)
        {
            try
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
