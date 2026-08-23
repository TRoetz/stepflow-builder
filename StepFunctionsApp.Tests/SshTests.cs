using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;
using Renci.SshNet;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Tests for the SSH host inventory (SshHostStore) and the pure command-resolution
    /// chain used by the ssh:// resource handler (SshCommandService.ResolveCommand).
    /// </summary>
    public class SshTests
    {
        // ── SshHostStore ───────────────────────────────────────────────

        private static string WriteTempInventory(string json)
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssh-hosts-tests");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>SSH.NET's PrivateKeyFile parses the key in its ctor, so key-auth tests need a real file.</summary>
        private static string WriteTempRsaKey()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssh-hosts-tests");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".pem");
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var pem = "-----BEGIN RSA PRIVATE KEY-----\n"
                + Convert.ToBase64String(rsa.ExportRSAPrivateKey(), Base64FormattingOptions.InsertLineBreaks)
                + "\n-----END RSA PRIVATE KEY-----";
            File.WriteAllText(path, pem);
            return path;
        }

        [Fact]
        public void Initialize_LoadsInventoryFromJson()
        {
            var path = WriteTempInventory("""
                [
                  { "Name": "web-01", "Host": "10.0.0.5", "Port": 22, "User": "deploy", "Password": "pw", "UseSshPassword": true },
                  { "Name": "db-01", "Host": "10.0.0.6", "User": "monitor", "SshKeyPath": "/keys/db", "UseSshKey": true, "UseSshPassword": false }
                ]
                """);

            try
            {
                var store = new SshHostStore();
                store.Initialize(path);

                Assert.Equal(2, store.Hosts.Count);
                var web = store.FindByName("web-01")!;
                Assert.Equal("10.0.0.5", web.Host);
                Assert.Equal(22, web.Port);
                Assert.True(web.UseSshPassword);

                // Port omitted → default 22; key-auth flags honored
                var db = store.FindByName("db-01")!;
                Assert.Equal(22, db.Port);
                Assert.True(db.UseSshKey);
                Assert.False(db.UseSshPassword);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Initialize_MissingFile_YieldsEmptyInventoryWithoutThrowing()
        {
            var store = new SshHostStore();
            store.Initialize(Path.Combine(Path.GetTempPath(), "ssh-hosts-tests", "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

            Assert.Empty(store.Hosts);
            Assert.Null(store.FindByName("anything"));
        }

        [Fact]
        public void Initialize_FiltersBlankNames()
        {
            var path = WriteTempInventory("""
                [
                  { "Name": "", "Host": "x" },
                  { "Name": "  ", "Host": "y" },
                  { "Name": "ok", "Host": "z" }
                ]
                """);

            try
            {
                var store = new SshHostStore();
                store.Initialize(path);
                Assert.Single(store.Hosts);
                Assert.Equal("ok", store.Hosts[0].Name);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void FindByName_IsCaseInsensitive()
        {
            var path = WriteTempInventory("""
                [ { "Name": "web-01", "Host": "h" } ]
                """);

            try
            {
                var store = new SshHostStore();
                store.Initialize(path);
                Assert.NotNull(store.FindByName("WEB-01"));
                Assert.NotNull(store.FindByName("Web-01"));
                Assert.Null(store.FindByName("web-02"));
                Assert.Null(store.FindByName("  "));
            }
            finally { File.Delete(path); }
        }

        // ── ResolveCommand (D4 resolution chain) ───────────────────────

        [Fact]
        public void ResolveCommand_RawStringInput_IsUsed()
        {
            Assert.Equal("df -h", SshCommandService.ResolveCommand(JValue.CreateString("  df -h ")));
        }

        [Fact]
        public void ResolveCommand_EmptyOrWhitespaceString_ReturnsNull()
        {
            Assert.Null(SshCommandService.ResolveCommand(JValue.CreateString("")));
            Assert.Null(SshCommandService.ResolveCommand(JValue.CreateString("   ")));
        }

        [Fact]
        public void ResolveCommand_CommandStringProperty_IsUsed()
        {
            var input = JObject.Parse("""{ "command": "uptime", "override": true }""");
            Assert.Equal("uptime", SshCommandService.ResolveCommand(input));
        }

        [Theory]
        [InlineData("text")]
        [InlineData("output")]
        [InlineData("answer")]
        [InlineData("content")]
        public void ResolveCommand_CommandObject_FallsBackThroughDocumentedFields(string field)
        {
            var input = JObject.Parse($"{{ \"command\": {{ \"{field}\": \"free -m\" }} }}");
            Assert.Equal("free -m", SshCommandService.ResolveCommand(input));
        }

        [Fact]
        public void ResolveCommand_CommandObject_PrefersTextOverOtherFields()
        {
            var input = JObject.Parse("""{ "command": { "text": "df -h", "output": "ignored" } }""");
            Assert.Equal("df -h", SshCommandService.ResolveCommand(input));
        }

        [Fact]
        public void ResolveCommand_RootLevelFallback_WhenNoCommandProperty()
        {
            // AI Text Gen object passed directly as the Task input (no .command wrapper)
            var input = JObject.Parse("""{ "output": "journalctl -n 20", "model": "qwen" }""");
            Assert.Equal("journalctl -n 20", SshCommandService.ResolveCommand(input));
        }

        [Fact]
        public void ResolveCommand_NothingResolvable_ReturnsNull()
        {
            Assert.Null(SshCommandService.ResolveCommand(JObject.Parse("""{ "unrelated": 1, "command": { "other": "x" } }""")));
            Assert.Null(SshCommandService.ResolveCommand(new JArray()));
            Assert.Null(SshCommandService.ResolveCommand(null));
        }

        // ── SshHost fetch fields + BuildConnectionInfo (D2) ────────────────

        [Fact]
        public void Initialize_ParsesOptionalFetchFields()
        {
            var path = WriteTempInventory("""
                [
                  { "Name": "db-01", "Host": "10.0.0.6", "User": "monitor", "Password": "pw", "FtpPort": 2121, "UseFtps": true, "Share": "backups" }
                ]
                """);

            try
            {
                var store = new SshHostStore();
                store.Initialize(path);

                var db = store.FindByName("db-01")!;
                Assert.Equal(2121, db.FtpPort);
                Assert.True(db.UseFtps);
                Assert.Equal("backups", db.Share);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Initialize_MissingFetchFields_KeepBackwardCompatibleDefaults()
        {
            var path = WriteTempInventory("""
                [
                  { "Name": "web-01", "Host": "10.0.0.5", "User": "deploy", "Password": "pw" }
                ]
                """);

            try
            {
                var store = new SshHostStore();
                store.Initialize(path);

                // Existing inventory files without the fetch fields parse unchanged.
                var web = store.FindByName("web-01")!;
                Assert.Equal(21, web.FtpPort);
                Assert.False(web.UseFtps);
                Assert.Equal("", web.Share);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BuildConnectionInfo_PasswordOnly_SinglePasswordAuth()
        {
            var host = new SshHost { Name = "h", Host = "10.0.0.5", Port = 22, User = "u", Password = "pw", UseSshPassword = true };

            var info = SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(30));

            Assert.Equal("10.0.0.5", info.Host);
            Assert.Equal(22, info.Port);
            Assert.Single(info.AuthenticationMethods);
            Assert.IsType<PasswordAuthenticationMethod>(info.AuthenticationMethods[0]);
        }

        [Fact]
        public void BuildConnectionInfo_KeyOnly_SingleKeyAuth()
        {
            var keyPath = WriteTempRsaKey();
            try
            {
                var host = new SshHost { Name = "h", Host = "10.0.0.6", Port = 2222, User = "u", UseSshKey = true, SshKeyPath = keyPath };

                var info = SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(30));

                Assert.Single(info.AuthenticationMethods);
                Assert.IsType<PrivateKeyAuthenticationMethod>(info.AuthenticationMethods[0]);
            }
            finally { File.Delete(keyPath); }
        }

        [Fact]
        public void BuildConnectionInfo_BothFlags_BothAuthMethods()
        {
            var keyPath = WriteTempRsaKey();
            try
            {
                var host = new SshHost { Name = "h", Host = "10.0.0.7", User = "u", Password = "pw", UseSshPassword = true, UseSshKey = true, SshKeyPath = keyPath };

                var info = SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(30));

                Assert.Equal(2, info.AuthenticationMethods.Count);
            }
            finally { File.Delete(keyPath); }
        }

        [Fact]
        public void BuildConnectionInfo_NoCreds_ThrowsNoCredentials()
        {
            var host = new SshHost { Name = "h", Host = "10.0.0.8", User = "u" }; // both flags false, no password/key path

            var ex = Assert.Throws<StepEngineException>(() => SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(30)));

            Assert.Equal("Ssh.NoCredentials", ex.ErrorCode);
        }
    }
}
