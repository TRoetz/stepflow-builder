using System;
using System.IO;
using System.Threading;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Tests for the fetch:// file-transfer service's pure logic: the validation chain
    /// (Fetch.* error codes, no network attempted), wildcard matching, remote-name
    /// sanitization, and xcopy argument construction. Live scp/sftp/ftp transfers need
    /// real servers — same bar as SshTests.
    /// </summary>
    public class FetchRemoteFilesTests
    {
        private const string OneHostJson = """[{"Name":"web-01","Host":"10.0.0.5","User":"u","Password":"p"}]""";

        private static (FetchRemoteFilesService svc, string path) NewService(string inventoryJson = "[]")
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssh-hosts-tests");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, inventoryJson);

            var store = new SshHostStore();
            store.Initialize(path);
            return (new FetchRemoteFilesService(store), path);
        }

        private static string TempDest() => Path.Combine(Path.GetTempPath(), "fetch-tests", Guid.NewGuid().ToString("N"));

        // ── Validation chain (Fetch.* codes; no network attempted) ────────

        [Fact]
        public async Task FetchAsync_UnknownHost_ThrowsHostNotFound()
        {
            var (svc, path) = NewService();
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("nope", "scp", "/tmp/a.txt", TempDest(), 10, CancellationToken.None));
                Assert.Equal("Fetch.HostNotFound", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task FetchAsync_UnsupportedProtocol_ThrowsUnsupportedProtocol()
        {
            var (svc, path) = NewService(OneHostJson);
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("web-01", "rsync", "/tmp/a.txt", TempDest(), 10, CancellationToken.None));
                Assert.Equal("Fetch.UnsupportedProtocol", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task FetchAsync_EmptySource_ThrowsNoSource()
        {
            var (svc, path) = NewService(OneHostJson);
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("web-01", "scp", "  ", TempDest(), 10, CancellationToken.None));
                Assert.Equal("Fetch.NoSource", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task FetchAsync_EmptyDest_ThrowsNoDest()
        {
            var (svc, path) = NewService(OneHostJson);
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("web-01", "scp", "/tmp/a.txt", "", 10, CancellationToken.None));
                Assert.Equal("Fetch.NoDest", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task FetchAsync_ScpWithWildcard_ThrowsGlobUnsupported()
        {
            var (svc, path) = NewService(OneHostJson);
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("web-01", "scp", "/var/log/*.txt", TempDest(), 10, CancellationToken.None));
                Assert.Equal("Fetch.GlobUnsupported", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public async Task FetchAsync_XcopyWithoutShare_ThrowsMissingShare()
        {
            // Share is checked before the Windows-OS check, so this holds on any platform.
            var (svc, path) = NewService(OneHostJson);
            try
            {
                var ex = await Assert.ThrowsAsync<StepEngineException>(() => svc.FetchAsync("web-01", "xcopy", @"\\web-01\backups\logs", TempDest(), 10, CancellationToken.None));
                Assert.Equal("Fetch.MissingShare", ex.ErrorCode);
            }
            finally { File.Delete(path); }
        }

        // ── Wildcard matching (D3) ────────────────────────────────────────

        [Theory]
        [InlineData("app.log", "app.log")]          // exact, no wildcard
        [InlineData("APP.LOG", "app.log")]          // case-insensitive
        [InlineData("*.log", "app.log")]            // leading run
        [InlineData("app-*.txt", "app-2026.txt")]   // middle run
        [InlineData("a?c", "abc")]                  // ? = exactly one char
        public void MatchesPattern_Matches(string pattern, string name) =>
            Assert.True(FetchRemoteFilesService.MatchesPattern(pattern, name));

        [Theory]
        [InlineData("app.log", "xapp.log")]         // anchored — no prefix match
        [InlineData("app.log", "app.log.bak")]      // anchored — no suffix match
        [InlineData("*.log", "app.txt")]            // extension mismatch
        [InlineData("a?c", "abbc")]                 // ? is not a run
        [InlineData("a?c", "ac")]                   // ? requires one char
        public void MatchesPattern_Rejects(string pattern, string name) =>
            Assert.False(FetchRemoteFilesService.MatchesPattern(pattern, name));

        [Theory]
        [InlineData("/tmp/*.txt", true)]
        [InlineData("/tmp/a?.log", true)]
        [InlineData("/tmp/plain.txt", false)]
        public void ContainsWildcard_DetectsStarAndQuestion(string path, bool expected) =>
            Assert.Equal(expected, FetchRemoteFilesService.ContainsWildcard(path));

        // ── Remote-name sanitization (D3) ─────────────────────────────────

        [Theory]
        [InlineData("app.log", "app.log")]
        [InlineData("/var/log/app.log", "app.log")]       // basename after last /
        [InlineData(@"C:\logs\app.log", "app.log")]       // backslash path → basename
        [InlineData("../etc/passwd", "unnamed")]          // traversal rejected outright
        [InlineData("a/../../b.txt", "unnamed")]          // embedded .. segment rejected
        [InlineData("", "unnamed")]                       // empty → unnamed
        public void SanitizeFileName_ReducesToSafeBasename(string input, string expected) =>
            Assert.Equal(expected, FetchRemoteFilesService.SanitizeFileName(input));

        // ── xcopy arguments (D1/D8) ───────────────────────────────────────

        [Fact]
        public void BuildXcopyArguments_QuotedUncWithTrailingBackslashAndFlags()
        {
            Assert.Equal(
                "\"\\\\web-01\\backups\\logs\" \"C:\\dest\\\" /I /Y",
                FetchRemoteFilesService.BuildXcopyArguments(@"\\web-01\backups\logs", @"C:\dest"));
        }
    }
}
