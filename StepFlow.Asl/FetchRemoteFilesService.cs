using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FETCH REMOTE FILES SERVICE
    // Fetches files from a curated remote host (the SshHostStore inventory — the same
    // ssh_hosts.json as the SSH Command node) over one of four protocols:
    //   scp   — single file or whole directory; no wildcards
    //   sftp  — wildcard patterns (* ?) in the last path segment, recursive directories
    //   ftp   — plain FTP / FTPS (host.UseFtps); wildcards via LIST basename parsing
    //   xcopy — Windows SMB share \\<host>\<share>\path; native wildcards
    // Paths come from node config against a curated inventory, so (unlike ssh://) there
    // is no AI safety check here. Remote file names are sanitized before use as local
    // paths to block traversal via crafted remote names.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class FetchedFile
    {
        public string RemotePath { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public long SizeBytes { get; set; }
    }

    public class FetchRemoteFilesResult
    {
        public string HostName { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string DestDir { get; set; } = "";
        public List<FetchedFile> Files { get; set; } = new();
        public double DurationMs { get; set; }
    }

    public class FetchRemoteFilesService
    {
        private const int BufferSize = 80 * 1024; // 80 KB transfer buffer (sftp/ftp)
        private const int MaxErrorDetailChars = 2000;
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        private readonly SshHostStore _hosts;
        private readonly ILogger<FetchRemoteFilesService>? _logger;

        public FetchRemoteFilesService(SshHostStore hosts, ILogger<FetchRemoteFilesService>? logger = null)
        {
            _hosts = hosts;
            _logger = logger;
        }

        /// <summary>
        /// Fetch files from the named curated host into destDir. Throws StepEngineException
        /// with Fetch.* error codes for every failure mode (host not found, unsupported
        /// protocol, missing source/dest, glob unsupported by protocol, missing share,
        /// non-Windows xcopy, timeout, download failure). Validation runs before any
        /// network connection is attempted.
        /// </summary>
        public async Task<FetchRemoteFilesResult> FetchAsync(string hostName, string protocol, string sourcePath, string destDir, int timeoutSeconds, CancellationToken ct)
        {
            if (timeoutSeconds < 1) timeoutSeconds = 120; // default when missing or invalid

            var host = _hosts.FindByName(hostName)
                ?? throw new StepEngineException("Fetch.HostNotFound", $"No curated SSH host named '{hostName}' in ssh_hosts.json");

            protocol = (protocol ?? "").Trim().ToLowerInvariant();
            if (protocol is not ("scp" or "sftp" or "ftp" or "xcopy"))
                throw new StepEngineException("Fetch.UnsupportedProtocol", $"Unsupported fetch protocol '{protocol}' — expected scp, sftp, ftp or xcopy");

            sourcePath = (sourcePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new StepEngineException("Fetch.NoSource", "No source path provided — set the node's 'sourcePath' parameter");

            destDir = (destDir ?? "").Trim();
            if (string.IsNullOrWhiteSpace(destDir))
                throw new StepEngineException("Fetch.NoDest", "No destination directory provided — set the node's 'destDir' parameter");

            Directory.CreateDirectory(destDir);

            var sw = Stopwatch.StartNew();
            List<FetchedFile> files;
            switch (protocol)
            {
                case "scp":
                    if (ContainsWildcard(sourcePath))
                        throw new StepEngineException("Fetch.GlobUnsupported", "SCP does not support wildcard patterns — use SFTP for * / ? matching");
                    files = await FetchScpAsync(host, sourcePath, destDir, timeoutSeconds, ct).ConfigureAwait(false);
                    break;

                case "sftp":
                    files = await FetchSftpAsync(host, sourcePath, destDir, timeoutSeconds, ct).ConfigureAwait(false);
                    break;

                case "ftp":
                    files = await FetchFtpAsync(host, sourcePath, destDir, timeoutSeconds, ct).ConfigureAwait(false);
                    break;

                default: // xcopy
                    if (string.IsNullOrWhiteSpace(host.Share))
                        throw new StepEngineException("Fetch.MissingShare", $"Host '{host.Name}' has no 'Share' configured in ssh_hosts.json — required for xcopy");
                    if (!OperatingSystem.IsWindows())
                        throw new StepEngineException("Fetch.UnsupportedOs", "xcopy requires a Windows machine (SMB share copy)");
                    files = await FetchXcopyAsync(host, sourcePath, destDir, timeoutSeconds, ct).ConfigureAwait(false);
                    break;
            }
            sw.Stop();

            _logger?.LogInformation("Fetched {Count} file(s) from {Host} via {Proto} into {Dest} in {Ms} ms", files.Count, host.Name, protocol, destDir, (long)sw.Elapsed.TotalMilliseconds);
            return new FetchRemoteFilesResult
            {
                HostName = host.Name,
                Protocol = protocol,
                SourcePath = sourcePath,
                DestDir = destDir,
                Files = files,
                DurationMs = sw.Elapsed.TotalMilliseconds
            };
        }

        // ── SCP ────────────────────────────────────────────────────────

        private async Task<List<FetchedFile>> FetchScpAsync(SshHost host, string remotePath, string destDir, int timeoutSeconds, CancellationToken ct)
        {
            using var linked = CreateLinkedCts(timeoutSeconds, ct);
            try
            {
                // Probe file-vs-dir over SFTP (fresh ConnectionInfo per client).
                bool isDirectory;
                using (var probe = new SftpClient(SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(timeoutSeconds))))
                {
                    await probe.ConnectAsync(linked.Token).ConfigureAwait(false);
                    try
                    {
                        var entry = probe.Get(remotePath); // throws when the path does not exist
                        isDirectory = entry.IsDirectory; // property in SSH.NET 2024.x
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        throw new StepEngineException("Fetch.NoSource", $"Cannot access remote path '{remotePath}' on {host.Host}: {Truncate(ex.Message)}");
                    }
                }

                using var scp = new ScpClient(SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(timeoutSeconds)));
                await scp.ConnectAsync(linked.Token).ConfigureAwait(false);

                // Sync-only Download API → run off-thread; Disconnect() on timeout/cancel unblocks it.
                var downloadTask = Task.Run(() =>
                {
                    if (isDirectory)
                        scp.Download(remotePath, new DirectoryInfo(destDir)); // contents land directly in destDir
                    else
                        scp.Download(remotePath, new FileInfo(Path.Combine(destDir, SanitizeFileName(remotePath))));
                }, CancellationToken.None);

                using var unblock = linked.Token.Register(() => { try { scp.Disconnect(); } catch { /* already closed */ } });
                await downloadTask.ConfigureAwait(false);

                // Report what the transfer placed in destDir (deterministic per-file listing).
                return isDirectory
                    ? WalkLocal(destDir, remotePath)
                    : new List<FetchedFile>
                    {
                        new()
                        {
                            RemotePath = remotePath,
                            LocalPath = Path.Combine(destDir, SanitizeFileName(remotePath)),
                            SizeBytes = new FileInfo(Path.Combine(destDir, SanitizeFileName(remotePath))).Length
                        }
                    };
            }
            catch (StepEngineException) { throw; }
            catch (Exception ex) { throw MapFailure(ex, ct, linked.IsCancellationRequested, $"SCP transfer from {host.Host}:{remotePath}", timeoutSeconds); }
        }

        // ── SFTP ───────────────────────────────────────────────────────

        private async Task<List<FetchedFile>> FetchSftpAsync(SshHost host, string remotePath, string destDir, int timeoutSeconds, CancellationToken ct)
        {
            using var linked = CreateLinkedCts(timeoutSeconds, ct);
            try
            {
                using var sftp = new SftpClient(SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(timeoutSeconds)));
                await sftp.ConnectAsync(linked.Token).ConfigureAwait(false);

                // Wildcard in the last path segment → list the parent and filter by name.
                var sep = remotePath.LastIndexOf('/');
                if (sep >= 0 && ContainsWildcard(remotePath[(sep + 1)..]))
                {
                    var pattern = remotePath[(sep + 1)..];
                    var parent = sep == 0 ? "/" : remotePath[..sep];
                    var files = new List<FetchedFile>();
                    try
                    {
                        await foreach (var entry in sftp.ListDirectoryAsync(parent, linked.Token).ConfigureAwait(false))
                        {
                            if (entry.Name is "." or ".." || entry.IsDirectory) continue;
                            if (!MatchesPattern(pattern, entry.Name)) continue;
                            ct.ThrowIfCancellationRequested();
                            files.Add(await DownloadSftpFileAsync(sftp, JoinRemote(parent, entry.Name), Path.Combine(destDir, SanitizeFileName(entry.Name)), linked.Token).ConfigureAwait(false));
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        throw new StepEngineException("Fetch.NoSource", $"Cannot access remote path '{remotePath}' on {host.Host}: {Truncate(ex.Message)}");
                    }
                    return files;
                }

                // No wildcard: probe file-vs-dir.
                ISftpFile probeEntry;
                try
                {
                    probeEntry = sftp.Get(remotePath); // throws when the path does not exist
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    throw new StepEngineException("Fetch.NoSource", $"Cannot access remote path '{remotePath}' on {host.Host}: {Truncate(ex.Message)}");
                }

                if (!probeEntry.IsDirectory)
                    return new List<FetchedFile>
                    {
                        await DownloadSftpFileAsync(sftp, remotePath, Path.Combine(destDir, SanitizeFileName(remotePath)), linked.Token).ConfigureAwait(false)
                    };

                // Recursive directory walk (no symlink-loop guard in v1).
                return await WalkRemoteDirectoryAsync(sftp, remotePath, destDir, linked.Token).ConfigureAwait(false);
            }
            catch (StepEngineException) { throw; }
            catch (Exception ex) { throw MapFailure(ex, ct, linked.IsCancellationRequested, $"SFTP transfer from {host.Host}:{remotePath}", timeoutSeconds); }
        }

        private async Task<List<FetchedFile>> WalkRemoteDirectoryAsync(SftpClient sftp, string remoteDir, string localBase, CancellationToken ct)
        {
            var results = new List<FetchedFile>();
            await foreach (var entry in sftp.ListDirectoryAsync(remoteDir, ct).ConfigureAwait(false))
            {
                if (entry.Name is "." or "..") continue;
                ct.ThrowIfCancellationRequested();
                var localName = SanitizeFileName(entry.Name);
                if (entry.IsDirectory)
                    results.AddRange(await WalkRemoteDirectoryAsync(sftp, JoinRemote(remoteDir, entry.Name), Path.Combine(localBase, localName), ct).ConfigureAwait(false));
                else
                    results.Add(await DownloadSftpFileAsync(sftp, JoinRemote(remoteDir, entry.Name), Path.Combine(localBase, localName), ct).ConfigureAwait(false));
            }
            return results;
        }

        private static async Task<FetchedFile> DownloadSftpFileAsync(SftpClient sftp, string remotePath, string localPath, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var bytes = 0L;
            await using var input = await sftp.OpenAsync(remotePath, FileMode.Open, FileAccess.Read, ct).ConfigureAwait(false);
            await using var output = File.Create(localPath);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested(); // real mid-transfer cancellation
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                bytes += read;
            }
            return new FetchedFile { RemotePath = remotePath, LocalPath = localPath, SizeBytes = bytes };
        }

        // ── FTP / FTPS ─────────────────────────────────────────────────

        private async Task<List<FetchedFile>> FetchFtpAsync(SshHost host, string sourcePath, string destDir, int timeoutSeconds, CancellationToken ct)
        {
            using var linked = CreateLinkedCts(timeoutSeconds, ct);
            try
            {
                var scheme = host.UseFtps ? "ftps" : "ftp";
                var path = sourcePath.StartsWith('/') ? sourcePath : "/" + sourcePath;

                // Wildcard in the last segment → LIST the parent, filter basenames, download each.
                var sep = path.LastIndexOf('/');
                string? pattern = null, listPath = null;
                if (sep >= 0 && ContainsWildcard(path[(sep + 1)..])) { pattern = path[(sep + 1)..]; listPath = sep == 0 ? "/" : path[..sep]; }
                else if (sep < 0 && ContainsWildcard(path)) { pattern = path; listPath = "/"; }

                var files = new List<FetchedFile>();
                if (pattern != null)
                {
                    foreach (var name in await FtpListBasenamesAsync(host, scheme, listPath!, timeoutSeconds).ConfigureAwait(false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!MatchesPattern(pattern, name)) continue;
                        files.Add(await FtpDownloadFileAsync(host, scheme, JoinRemote(listPath!, name), destDir, name, timeoutSeconds, linked.Token).ConfigureAwait(false));
                    }
                }
                else
                {
                    files.Add(await FtpDownloadFileAsync(host, scheme, path, destDir, SanitizeFileName(path), timeoutSeconds, linked.Token).ConfigureAwait(false));
                }
                return files;
            }
            catch (StepEngineException) { throw; }
            catch (Exception ex) { throw MapFailure(ex, ct, linked.IsCancellationRequested, $"FTP fetch from {host.Host}:{sourcePath}", timeoutSeconds); }
        }

        private static async Task<List<string>> FtpListBasenamesAsync(SshHost host, string scheme, string listPath, int timeoutSeconds)
        {
            var request = CreateFtpRequest(host, scheme, listPath, "LIST", timeoutSeconds); // FtpMethod.List — string-based in .NET 10
            WebResponse response;
            try
            {
                response = await request.GetResponseAsync().ConfigureAwait(false);
            }
            catch (WebException ex)
            {
                throw new StepEngineException("Fetch.DownloadFailed", $"FTP listing of '{listPath}' on {host.Host} failed: {Truncate(ex.Message)}");
            }

            var names = new List<string>();
            using (response)
            {
                await using var stream = response.GetResponseStream()!;
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { Length: > 0 } line)
                {
                    // Basename = last whitespace-separated token of the LIST entry.
                    var name = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (!string.IsNullOrEmpty(name) && name is not ("." or "..")) names.Add(name);
                }
            }
            return names;
        }

        private static async Task<FetchedFile> FtpDownloadFileAsync(SshHost host, string scheme, string remotePath, string destDir, string localName, int timeoutSeconds, CancellationToken ct)
        {
            var request = CreateFtpRequest(host, scheme, remotePath, "GET", timeoutSeconds); // FtpMethod.Download — string-based in .NET 10

            WebResponse response;
            try
            {
                response = await request.GetResponseAsync().ConfigureAwait(false);
            }
            catch (WebException ex) when (!ct.IsCancellationRequested)
            {
                // A 5xx on a direct download is the directory case in v1.
                if (ex.Status == WebExceptionStatus.ProtocolError) // 5xx on a direct download = the directory case in v1
                    throw new StepEngineException("Fetch.DownloadFailed", "FTP v1 does not support recursive directory fetch — use an explicit file path or a wildcard pattern");
                throw new StepEngineException("Fetch.DownloadFailed", $"FTP download of '{remotePath}' from {host.Host} failed: {Truncate(ex.Message)}");
            }

            ct.ThrowIfCancellationRequested();
            var localPath = Path.Combine(destDir, SanitizeFileName(localName));
            var bytes = 0L;
            using (response)
            {
                await using var input = response.GetResponseStream()!;
                await using var output = File.Create(localPath);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    bytes += read;
                }
            }
            return new FetchedFile { RemotePath = remotePath, LocalPath = localPath, SizeBytes = bytes };
        }

        private static FtpWebRequest CreateFtpRequest(SshHost host, string scheme, string path, string method, int timeoutSeconds)
        {
            var port = host.FtpPort > 0 ? host.FtpPort : 21;
            var url = $"{scheme}://{host.Host}:{port}/{EscapeFtpPath(path)}";
            var request = (FtpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Credentials = new NetworkCredential(host.User, host.Password);
            if (host.UseFtps) request.EnableSsl = true; // standard certificate validation — no bypass in v1
            request.ReadWriteTimeout = timeoutSeconds * 1000;
            return request;
        }

        private static string EscapeFtpPath(string path)
        {
            var segments = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++) segments[i] = Uri.EscapeDataString(segments[i]);
            return string.Join("/", segments);
        }

        // ── XCOPY (Windows SMB share) ──────────────────────────────────

        private async Task<List<FetchedFile>> FetchXcopyAsync(SshHost host, string sourcePath, string destDir, int timeoutSeconds, CancellationToken ct)
        {
            using var linked = CreateLinkedCts(timeoutSeconds, ct);

            // Remote spec: \\<host>\<share>\sourcePath — wildcards pass through natively.
            var remoteSpec = $"\\\\{host.Host}\\{host.Share}\\" + sourcePath.TrimStart('/', '\\');

            // Pre-run snapshot so re-runs report only new/changed entries.
            var before = SnapshotDirectory(destDir);

            var psi = new ProcessStartInfo
            {
                FileName = "xcopy",
                Arguments = BuildXcopyArguments(remoteSpec, destDir),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi) ?? throw new StepEngineException("Fetch.DownloadFailed", "Failed to start xcopy");
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                // Kill the whole process tree on timeout or external cancellation.
                using var unblock = linked.Token.Register(() => { try { process.Kill(true); } catch { /* already exited */ } });
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

                if (process.ExitCode != 0)
                    throw new StepEngineException("Fetch.DownloadFailed", $"xcopy exit {process.ExitCode}: {Truncate(await stderrTask.ConfigureAwait(false))}");

                var after = SnapshotDirectory(destDir);
                return after
                    .Where(kv => !before.TryGetValue(kv.Key, out var prev) || prev != kv.Value)
                    .Select(kv => new FetchedFile
                    {
                        RemotePath = $"{remoteSpec}\\{kv.Key}",
                        LocalPath = Path.Combine(destDir, kv.Key),
                        SizeBytes = kv.Value.Size
                    })
                    .ToList();
            }
            catch (StepEngineException) { throw; }
            catch (Exception ex) { throw MapFailure(ex, ct, linked.IsCancellationRequested, $"xcopy from {remoteSpec}", timeoutSeconds); }
        }

        private static Dictionary<string, (long Size, DateTime Mtime)> SnapshotDirectory(string dir)
        {
            var snapshot = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return snapshot;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                snapshot[Path.GetRelativePath(dir, file).Replace('\\', '/')] = (info.Length, info.LastWriteTimeUtc);
            }
            return snapshot;
        }

        // ── Pure helpers (unit-testable, no network) ───────────────────

        /// <summary>True when the path contains * or ? wildcards.</summary>
        public static bool ContainsWildcard(string path) => path.Contains('*') || path.Contains('?');

        /// <summary>
        /// Wildcard match: * → any run of characters, ? → exactly one character; anchored
        /// full-string comparison, case-insensitive. Patterns without wildcards must match
        /// exactly (case-insensitive).
        /// </summary>
        public static bool MatchesPattern(string pattern, string name)
        {
            if (!ContainsWildcard(pattern))
                return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);

            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".+").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Reduce a remote file name to a safe local file name: reject any ".." path segment
        /// outright (traversal), take the basename after the last / or \, strip invalid
        /// filename characters; empty/. /.. → "unnamed".
        /// </summary>
        public static string SanitizeFileName(string remoteName)
        {
            var name = (remoteName ?? "").Replace('\\', '/');
            if (name.Split('/').Any(segment => segment == "..")) return "unnamed";

            var idx = name.LastIndexOf('/');
            if (idx >= 0) name = name[(idx + 1)..];

            var cleaned = new string(name.Where(c => !InvalidFileNameChars.Contains(c)).ToArray());
            return cleaned is "" or "." or ".." ? "unnamed" : cleaned;
        }

        /// <summary>xcopy argument line: quoted UNC source, quoted dest with trailing backslash, /I (dest is a directory) /Y (overwrite without prompt).</summary>
        public static string BuildXcopyArguments(string source, string destDir) =>
            $"\"{source}\" \"{destDir}\\\" /I /Y";

        // ── Shared plumbing ────────────────────────────────────────────

        private static CancellationTokenSource CreateLinkedCts(int timeoutSeconds, CancellationToken outer)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            return cts;
        }

        /// <summary>
        /// Map a protocol failure to Fetch.* codes and return it for an explicit `throw`:
        /// external cancellation propagates raw (same convention as SshCommandService), timer
        /// expiry → Fetch.Timeout, anything else → Fetch.DownloadFailed. An explicit throw keeps
        /// definite-return analysis trivial in this compiler ([DoesNotReturn] is not honored).
        /// </summary>
        private static Exception MapFailure(Exception ex, CancellationToken outerCt, bool timedOut, string context, int timeoutSeconds)
        {
            if (outerCt.IsCancellationRequested)
                return ex is OperationCanceledException oce ? oce : new OperationCanceledException($"Fetch cancelled during {context}", ex, outerCt);
            if (timedOut)
                return new StepEngineException("Fetch.Timeout", $"{context} timed out after {timeoutSeconds}s");
            return new StepEngineException("Fetch.DownloadFailed", $"{context} failed: {Truncate(ex.Message)}");
        }

        private static List<FetchedFile> WalkLocal(string destDir, string remoteRoot)
        {
            var files = new List<FetchedFile>();
            foreach (var file in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(destDir, file).Replace('\\', '/');
                files.Add(new FetchedFile
                {
                    RemotePath = $"{remoteRoot}/{rel}",
                    LocalPath = file,
                    SizeBytes = new FileInfo(file).Length
                });
            }
            return files;
        }

        private static string JoinRemote(string dir, string name) => dir.EndsWith('/') ? dir + name : dir + "/" + name;

        private static string Truncate(string? text) =>
            string.IsNullOrEmpty(text) ? "" : (text.Length <= MaxErrorDetailChars ? text : text[..MaxErrorDetailChars] + "…");
    }
}
