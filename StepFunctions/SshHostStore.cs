using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Renci.SshNet;
using ConnectionInfo = Renci.SshNet.ConnectionInfo; // disambiguate from Microsoft.AspNetCore.Http.ConnectionInfo (Web SDK implicit usings)
namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SSH HOST STORE
    // Curated inventory of remote hosts that the ssh:// and fetch:// resource schemes may target.
    // Loaded from a JSON file (ssh_hosts.json) at startup; missing file → empty
    // inventory + warning log so the app still starts. The real file is gitignored
    // because it may contain credentials — see ssh_hosts.example.json for format.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class SshHost
    {
        [JsonProperty("Name")]   public string Name { get; set; } = "";
        [JsonProperty("Host")]   public string Host { get; set; } = "";
        [JsonProperty("Port")]   public int Port { get; set; } = 22;
        [JsonProperty("User")]   public string User { get; set; } = "";
        [JsonProperty("Password")] public string Password { get; set; } = "";
        [JsonProperty("SshKeyPath")] public string SshKeyPath { get; set; } = "";
        [JsonProperty("UseSshKey")] public bool UseSshKey { get; set; }
        [JsonProperty("UseSshPassword")] public bool UseSshPassword { get; set; } = true;
        [JsonProperty("FtpPort")] public int FtpPort { get; set; } = 21;
        [JsonProperty("UseFtps")] public bool UseFtps { get; set; }
        [JsonProperty("Share")] public string Share { get; set; } = "";
    }
    public class SshHostStore
    {
        private readonly ILogger<SshHostStore>? _logger;
        private List<SshHost> _hosts = new();

        public SshHostStore(ILogger<SshHostStore>? logger = null) => _logger = logger;

        /// <summary>
        /// Load the inventory from a path relative to the current directory (content root).
        /// Missing file → empty inventory + warning log (app still starts).
        /// </summary>
        public void Initialize(string path)
        {
            if (!File.Exists(path))
            {
                _logger?.LogWarning("SSH host inventory '{Path}' not found — ssh:// resources will fail with Ssh.HostNotFound until the file is created (see ssh_hosts.example.json)", path);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<List<SshHost>>(json) ?? new List<SshHost>();
                _hosts = loaded.Where(h => !string.IsNullOrWhiteSpace(h.Name)).ToList();
                _logger?.LogInformation("Loaded {Count} SSH host(s) from {Path}", _hosts.Count, path);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load SSH host inventory '{Path}' — continuing with empty inventory", path);
            }
        }

        public IReadOnlyList<SshHost> Hosts => _hosts;

        /// <summary>Case-insensitive lookup by Name.</summary>
        public SshHost? FindByName(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : _hosts.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Build a fresh ConnectionInfo for one SSH.NET client from an inventory entry
        /// (password and/or key auth). Fresh per client — reusing one across clients
        /// corrupts state in SSH.NET 2024.x. Shared by the ssh:// command service and
        /// the fetch:// file-transfer service so both build auth identically.
        /// </summary>
        internal static ConnectionInfo BuildConnectionInfo(SshHost host, TimeSpan timeout)
        {
            var auths = new List<AuthenticationMethod>();
            if (host.UseSshPassword && !string.IsNullOrEmpty(host.Password))
                auths.Add(new PasswordAuthenticationMethod(host.User, host.Password));
            if (host.UseSshKey && !string.IsNullOrEmpty(host.SshKeyPath))
                auths.Add(new PrivateKeyAuthenticationMethod(host.User, new IPrivateKeySource[] { new PrivateKeyFile(host.SshKeyPath) }));

            if (auths.Count == 0)
                throw new StepEngineException("Ssh.NoCredentials", $"Host '{host.Name}' has no credentials configured — set Password/UseSshPassword or SshKeyPath/UseSshKey in ssh_hosts.json");

            return new ConnectionInfo(host.Host, host.Port, host.User, auths.ToArray())
            {
                Timeout = timeout
            };
        }
    }
}
