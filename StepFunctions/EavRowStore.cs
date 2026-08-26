using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // EAV ROW STORE — captured form submissions stay file-based (per the ask): one JSON
    // array per attribute domain under eav-data/{domainName}.json. Lock + atomic rewrite;
    // missing dir ⇒ empty store + warning log. Rows are append-only here; consumers read
    // via ListRows and the EAV mapper turns them into rule payloads.
    // ═══════════════════════════════════════════════════════════════════════════════

    public sealed class EavRow
    {
        /// <summary>Stable row key (guid, no dashes) — assigned on append if empty.</summary>
        public string RowKeyId { get; set; } = Guid.NewGuid().ToString("N");
        /// <summary>Optional entity id the row belongs to (null for pure capture).</summary>
        public object? EntityId { get; set; }
        public string? EntityType { get; set; }
        /// <summary>Human-task / form-capture task id that produced this row.</summary>
        public string? SourceTaskId { get; set; }
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Captured attribute values keyed by AttributeName.</summary>
        public JObject Values { get; set; } = new();
    }

    public class EavRowStore
    {
        private static readonly Regex SafeDomainRegex = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

        private string? _directory;
        private readonly ILogger<EavRowStore>? _logger;
        private readonly object _lock = new();

        public EavRowStore(ILogger<EavRowStore>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>Points the store at its directory (created if missing).</summary>
        public void Initialize(string directory)
        {
            _directory = Path.GetFullPath(directory);
            if (!Directory.Exists(_directory))
            {
                _logger?.LogWarning("EAV data directory '{Dir}' does not exist — creating it", _directory);
                Directory.CreateDirectory(_directory);
            }
        }

        private string RequirePath(string domainName)
        {
            if (_directory == null) throw new InvalidOperationException("EavRowStore.Initialize was not called");
            if (!SafeDomainRegex.IsMatch(domainName)) throw new ArgumentException($"Invalid attribute domain name: '{domainName}'");
            return Path.Combine(_directory, $"{domainName}.json");
        }

        /// <summary>Appends a row to the domain's file (assigns RowKeyId when empty).</summary>
        public void AppendRow(string domainName, EavRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            var path = RequirePath(domainName);

            lock (_lock)
            {
                var rows = LoadRows(path);
                if (string.IsNullOrEmpty(row.RowKeyId)) row.RowKeyId = Guid.NewGuid().ToString("N");
                rows.Add(row);
                WriteRows(path, rows);
            }
        }

        /// <summary>All captured rows for a domain, in append order.</summary>
        public IReadOnlyList<EavRow> ListRows(string domainName)
        {
            var path = RequirePath(domainName);
            lock (_lock)
                return LoadRows(path);
        }

        private static List<EavRow> LoadRows(string path)
        {
            if (!File.Exists(path)) return new();
            try
            {
                // DateParseHandling.None: captured values are data, not tokens to reinterpret.
                // ISO-8601 date strings must round-trip as plain strings (culture-free); the typed
                // CapturedAtUtc property still deserializes through its own converter.
                var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
                return JsonConvert.DeserializeObject<List<EavRow>>(File.ReadAllText(path), settings) ?? new();
            }
            catch (Exception ex)
            {
                // Corrupt file: surface loudly but don't crash the flow on read.
                throw new InvalidOperationException($"EAV data file '{path}' is corrupted", ex);
            }
        }

        private static void WriteRows(string path, List<EavRow> rows)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(rows, Formatting.Indented));
            File.Move(temp, path, overwrite: true);
        }
    }
}
