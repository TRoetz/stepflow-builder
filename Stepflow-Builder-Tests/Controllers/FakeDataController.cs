using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stepflow_Builder_Tests.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // FAKE DATA API — deterministic mock endpoints for testing flows locally.
    //
    // Every value is derived from request parameters (never the wall clock), so flow
    // runs and tests are fully reproducible. Endpoints live under /api/fake/*:
    //
    //   GET  /api/fake/weather?city=&units=          weather report JSON
    //   GET  /api/fake/gold-price?currency=&amount=  gold price per troy ounce
    //   GET  /api/fake/exchange-rate?from=&to=&amt=  currency conversion (NZD/USD/...)
    //   GET  /api/fake/json/records?count=&seed=     large-volume nested JSON dataset
    //   GET  /api/fake/xml/invoices?count=&seed=     XML invoice document
    //   POST /api/fake/csv/import                    CSV file import (multipart, text/csv or JSON body)
    //   GET  /api/fake/csv/sample?rows=&seed=        raw CSV "file" to download and re-import
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api/fake")]
    public class FakeDataController : ControllerBase
    {
        // ── Shared deterministic data pools ───────────────────────────────────────

        private static readonly string[] WeatherConditions =
            { "sunny", "partly-cloudy", "cloudy", "overcast", "light-rain", "thunderstorms", "fog" };

        private static readonly string[] Regions = { "APAC", "EMEA", "AMER", "LATAM" };

        private static readonly string[] Departments =
            { "Engineering", "Sales", "Support", "Marketing", "Finance" };

        private static readonly string[] FirstNames =
            { "Aroha", "Ben", "Chloe", "Diego", "Ella", "Finn", "Grace", "Hana", "Ivan", "Jade", "Kai", "Lena" };

        private static readonly string[] LastNames =
            { "Anderson", "Brown", "Carter", "Dawson", "Evans", "Foster", "Gray", "Hughes", "Irwin", "Jones", "Kelly", "Lopez" };

        private static readonly string[] Tags =
            { "vip", "trial", "enterprise", "beta", "renewal", "churn-risk", "upsell", "internal" };

        private static readonly string[] Cities =
            { "Wellington", "Auckland", "Christchurch", "Hamilton", "Dunedin", "Tauranga" };

        private static readonly string[] StreetNames =
            { "Cuba", "Lambton", "Victoria", "Cube", "Manners", "The Terrace" };

        private static readonly string[] Customers =
            { "Acme Traders Ltd", "Bayside Retail Group", "Coastal Freight Co", "Dunedin Manufacturing", "Eastgate Supplies", "Fjordline Logistics" };

        private static readonly string[] Products =
            { "Widget Pro", "Gadget Mini", "Sensor Array X2", "Cable Kit 5m", "Power Adapter 65W", "Mounting Bracket, Heavy Duty" };

        // ── Weather ───────────────────────────────────────────────────────────────

        [HttpGet("weather")]
        public IActionResult GetWeather([FromQuery] string? city, [FromQuery] string units = "celsius")
        {
            if (string.IsNullOrWhiteSpace(city))
                return BadRequest(new { error = "'city' query parameter is required" });

            var seed = Fnv1a(city.Trim().ToLowerInvariant());
            var rng = new Random((int)seed);

            double celsius = Math.Round(-5 + rng.NextDouble() * 43, 1); // -5..38 °C
            bool fahrenheit = units.Equals("fahrenheit", StringComparison.OrdinalIgnoreCase) || units.Equals("F", StringComparison.OrdinalIgnoreCase);
            double temperature = fahrenheit ? Math.Round(celsius * 9.0 / 5.0 + 32.0, 1) : celsius;

            var forecast = new JArray();
            for (int i = 0; i < 5; i++)
            {
                double high = Math.Round(temperature + rng.NextDouble() * 6 - 1, 1);
                double low = Math.Round(high - (3 + rng.NextDouble() * 8), 1);
                forecast.Add(new JObject
                {
                    ["day"] = i == 0 ? "today" : $"day-{i}",
                    ["high"] = high,
                    ["low"] = low,
                    ["condition"] = WeatherConditions[rng.Next(WeatherConditions.Length)]
                });
            }

            double windSpeedKph = Math.Round(rng.NextDouble() * 40, 1);
            return Ok(new JObject
            {
                ["city"] = city.Trim(),
                ["units"] = fahrenheit ? "fahrenheit" : "celsius",
                ["temperature"] = temperature,
                ["feelsLike"] = Math.Round(temperature + (windSpeedKph > 20 ? -1.5 : 0.5), 1),
                ["condition"] = WeatherConditions[rng.Next(WeatherConditions.Length)],
                ["humidityPercent"] = 30 + rng.Next(61), // 30..90 %
                ["windSpeedKph"] = windSpeedKph,
                ["observedAt"] = "2026-08-19T06:30:00Z", // fixed for determinism
                ["forecast"] = forecast
            });
        }

        // ── Gold price ────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, double> GoldPricesPerOunce = new()
        {
            ["USD"] = 2650.42, ["EUR"] = 2438.90, ["NZD"] = 4521.75, ["AUD"] = 4012.30, ["GBP"] = 2105.60
        };

        private static readonly Dictionary<string, double> GoldChangePercent24h = new()
        {
            ["USD"] = 0.84, ["EUR"] = 0.79, ["NZD"] = 0.91, ["AUD"] = 0.82, ["GBP"] = 0.88
        };

        [HttpGet("gold-price")]
        public IActionResult GetGoldPrice([FromQuery] string currency = "USD", [FromQuery] double? amount = null)
        {
            var code = (currency ?? "").Trim().ToUpperInvariant();
            if (!GoldPricesPerOunce.TryGetValue(code, out var price))
                return BadRequest(new
                {
                    error = $"Unsupported currency '{currency}'",
                    supported = GoldPricesPerOunce.Keys.OrderBy(k => k).ToArray()
                });

            double ounces = amount ?? 1;
            if (ounces <= 0)
                return BadRequest(new { error = "'amount' must be > 0" });

            return Ok(new JObject
            {
                ["metal"] = "gold",
                ["purity"] = "24k (999.9)",
                ["unit"] = "troy_ounce",
                ["currency"] = code,
                ["pricePerOunce"] = price,
                ["amount"] = ounces,
                ["totalValue"] = Math.Round(price * ounces, 2),
                ["changePercent24h"] = GoldChangePercent24h[code]
            });
        }

        // ── Exchange rate ─────────────────────────────────────────────────────────

        /// <summary>Units of each currency per 1 USD — cross rates are derived from this table.</summary>
        private static readonly Dictionary<string, double> UnitsPerUsd = new()
        {
            ["USD"] = 1.0, ["NZD"] = 1.6327, ["EUR"] = 1.0853, ["AUD"] = 1.5177, ["GBP"] = 0.7855
        };

        [HttpGet("exchange-rate")]
        public IActionResult GetExchangeRate([FromQuery] string? from, [FromQuery] string? to, [FromQuery] double? amount = null)
        {
            var f = (from ?? "").Trim().ToUpperInvariant();
            var t = (to ?? "").Trim().ToUpperInvariant();

            if (f.Length == 0 || t.Length == 0)
                return BadRequest(new { error = "'from' and 'to' query parameters are required" });

            double value = amount ?? 1;
            if (value <= 0)
                return BadRequest(new { error = "'amount' must be > 0" });

            if (!UnitsPerUsd.ContainsKey(f))
                return BadRequest(new { error = $"Unsupported 'from' currency '{from}'", supported = UnitsPerUsd.Keys.OrderBy(k => k).ToArray() });
            if (!UnitsPerUsd.ContainsKey(t))
                return BadRequest(new { error = $"Unsupported 'to' currency '{to}'", supported = UnitsPerUsd.Keys.OrderBy(k => k).ToArray() });

            double rate = UnitsPerUsd[t] / UnitsPerUsd[f];
            return Ok(new JObject
            {
                ["from"] = f,
                ["to"] = t,
                ["amount"] = value,
                ["baseRate"] = Math.Round(rate, 6),
                ["convertedAmount"] = Math.Round(value * rate, 4),
                ["timestamp"] = "2026-08-19T00:00:00Z" // fixed for determinism
            });
        }

        // ── Large-volume JSON dataset ─────────────────────────────────────────────

        private const int MaxJsonRecords = 10_000;

        [HttpGet("json/records")]
        public IActionResult GetJsonRecords([FromQuery] int? count = null, [FromQuery] int seed = 42)
        {
            int requested = count ?? 1000;
            if (requested < 1) requested = 1;
            bool clamped = requested > MaxJsonRecords;
            if (clamped) requested = MaxJsonRecords;

            var rng = new Random(seed);
            var records = new JArray();
            for (int i = 0; i < requested; i++)
            {
                string first = FirstNames[rng.Next(FirstNames.Length)];
                string last = LastNames[rng.Next(LastNames.Length)];

                var record = new JObject
                {
                    ["id"] = $"REC-{seed:D4}-{i + 1:D6}",
                    ["name"] = $"{first} {last}",
                    ["email"] = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{i}@example.com",
                    ["region"] = Regions[rng.Next(Regions.Length)],
                    ["department"] = Departments[rng.Next(Departments.Length)],
                    ["salary"] = Math.Round(35000 + rng.NextDouble() * 95000, 2),
                    ["joinedAt"] = $"20{15 + rng.Next(11)}-{rng.Next(1, 13):D2}-{rng.Next(1, 29):D2}",
                    ["active"] = rng.NextDouble() > 0.15
                };

                var tags = new JArray();
                for (int t = 0; t < 1 + rng.Next(4); t++) tags.Add(Tags[rng.Next(Tags.Length)]);
                record["tags"] = tags;

                record["address"] = new JObject
                {
                    ["street"] = $"{rng.Next(1, 500)} {StreetNames[rng.Next(StreetNames.Length)]} St",
                    ["city"] = Cities[rng.Next(Cities.Length)],
                    ["zip"] = rng.Next(1000, 9999).ToString(CultureInfo.InvariantCulture)
                };

                var orders = new JArray();
                for (int o = 0; o < 1 + rng.Next(3); o++)
                {
                    var items = new JArray();
                    double orderTotal = 0;
                    for (int it = 0; it < 1 + rng.Next(3); it++)
                    {
                        double unitPrice = Math.Round(5 + rng.NextDouble() * 495, 2);
                        int quantity = 1 + rng.Next(5);
                        orderTotal += unitPrice * quantity;
                        items.Add(new JObject
                        {
                            ["sku"] = $"SKU-{rng.Next(1000):D3}",
                            ["quantity"] = quantity,
                            ["unitPrice"] = unitPrice
                        });
                    }
                    orders.Add(new JObject
                    {
                        ["orderId"] = $"ORD-{seed:D4}-{i + 1:D6}-{o + 1}",
                        ["total"] = Math.Round(orderTotal, 2),
                        ["items"] = items
                    });
                }
                record["orders"] = orders;

                records.Add(record);
            }

            return Ok(new JObject
            {
                ["source"] = "fake-data-api",
                ["seed"] = seed,
                ["requested"] = count ?? requested,
                ["count"] = requested,
                ["clampedToMax"] = clamped,
                ["records"] = records
            });
        }

        // ── XML invoices ──────────────────────────────────────────────────────────

        [HttpGet("xml/invoices")]
        public IActionResult GetXmlInvoices([FromQuery] int? count = null, [FromQuery] int seed = 7)
        {
            int requested = count ?? 10;
            if (requested < 1) requested = 1;
            if (requested > 500) requested = 500;

            var rng = new Random(seed);
            var invoices = new XElement("Invoices",
                new XAttribute("currency", "USD"),
                new XAttribute("count", requested));

            for (int i = 1; i <= requested; i++)
            {
                double invoiceTotal = 0;
                var lineItems = new XElement("LineItems");
                for (int j = 0; j < 1 + rng.Next(4); j++)
                {
                    double unitPrice = Math.Round(10 + rng.NextDouble() * 990, 2);
                    int quantity = 1 + rng.Next(10);
                    double lineTotal = Math.Round(unitPrice * quantity, 2);
                    invoiceTotal += lineTotal;

                    lineItems.Add(new XElement("Item",
                        new XAttribute("sku", $"SKU-{rng.Next(1000):D3}"),
                        new XElement("Description", Products[rng.Next(Products.Length)]),
                        new XElement("Quantity", quantity.ToString(CultureInfo.InvariantCulture)),
                        new XElement("UnitPrice", unitPrice.ToString("F2", CultureInfo.InvariantCulture)),
                        new XElement("LineTotal", lineTotal.ToString("F2", CultureInfo.InvariantCulture))));
                }

                invoices.Add(new XElement("Invoice",
                    new XAttribute("id", $"INV-{seed:D3}-{i:D4}"),
                    new XAttribute("date", $"2026-0{1 + rng.Next(8)}-{rng.Next(1, 29):D2}"),
                    new XElement("Customer", Customers[rng.Next(Customers.Length)]),
                    lineItems,
                    new XElement("Total", Math.Round(invoiceTotal, 2).ToString("F2", CultureInfo.InvariantCulture))));
            }

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), invoices);
            return Content(doc.ToString(), "application/xml");
        }

        // ── CSV sample ("file" to download) ───────────────────────────────────────

        [HttpGet("csv/sample")]
        public IActionResult GetCsvSample([FromQuery] int? rows = null, [FromQuery] int seed = 1)
        {
            int requested = rows ?? 25;
            if (requested < 1) requested = 1;
            var rng = new Random(seed);
            var sb = new StringBuilder();
            sb.Append("order_id,customer,region,product,quantity,unit_price,amount,ordered_at\n");
            for (int i = 1; i <= requested; i++)
            {
                double unitPrice = Math.Round(5 + rng.NextDouble() * 495, 2);
                int quantity = 1 + rng.Next(10);
                double amount = Math.Round(unitPrice * quantity, 2);

                sb.Append($"ORD-{seed:D3}-{i:D5},")
                  .Append(Customers[rng.Next(Customers.Length)]).Append(',')
                  .Append(Regions[rng.Next(Regions.Length)]).Append(',')
                  .Append('"').Append(Products[rng.Next(Products.Length)]).Append("\",")
                  .Append(quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(unitPrice.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(amount.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append($"2026-0{1 + rng.Next(8)}-{rng.Next(1, 29):D2}T{rng.Next(0, 24):D2}:{rng.Next(0, 60):D2}:00Z")
                  .Append('\n');
            }

            return Content(sb.ToString(), "text/csv");
        }


        // ── CSV import (the file-reading process) ─────────────────────────────────

        [HttpPost("csv/import")]
        public async Task<IActionResult> ImportCsv()
        {
            string csv;
            string source;
            char delimiter = ',';

            if (Request.HasFormContentType)
            {
                // Multipart file upload: curl -F "file=@orders.csv" http://localhost:5095/api/fake/csv/import
                var form = await Request.ReadFormAsync();
                if (form.Files.Count == 0)
                    return BadRequest(new { error = "Multipart request must include a file part" });

                var file = form.Files[0];
                using var reader = new StreamReader(file.OpenReadStream());
                csv = await reader.ReadToEndAsync();
                source = string.IsNullOrEmpty(file.FileName) ? "upload" : file.FileName;
                var delim = ParseDelimiter(form["delimiter"].FirstOrDefault());
                if (delim == null) return BadRequest(new { error = "'delimiter' must be a single non-whitespace character" });
                delimiter = delim.Value;
            }
            else if ((Request.ContentType ?? "").StartsWith("text/csv", StringComparison.OrdinalIgnoreCase))
            {
                // Raw CSV body: curl -T orders.csv http://localhost:5095/api/fake/csv/import
                using var reader = new StreamReader(Request.Body);
                csv = await reader.ReadToEndAsync();
                source = "raw-body";
            }
            else
            {
                // JSON body (what the flow engine's HTTP invoker sends): { "csv": "...", "delimiter": "," }
                using var reader = new StreamReader(Request.Body);
                string bodyText = await reader.ReadToEndAsync();

                JObject? body;
                try
                {
                    body = string.IsNullOrWhiteSpace(bodyText) ? null : JObject.Parse(bodyText);
                }
                catch (JsonException ex)
                {
                    return BadRequest(new { error = $"Invalid JSON body: {ex.Message}" });
                }

                if (body == null || string.IsNullOrWhiteSpace(body["csv"]?.ToString()))
                    return BadRequest(new { error = "JSON body must contain a non-empty 'csv' field" });

                csv = body["csv"]!.ToString()!;
                source = body["fileName"]?.ToString() ?? "json-body";
                var delim = ParseDelimiter(body["delimiter"]?.ToString());
                if (delim == null) return BadRequest(new { error = "'delimiter' must be a single non-whitespace character" });
                delimiter = delim.Value;
            }

            if (string.IsNullOrWhiteSpace(csv))
                return BadRequest(new { error = "CSV content is empty" });

            var parsed = CsvParser.Parse(csv, delimiter);
            if (parsed.Count == 0)
                return BadRequest(new { error = "CSV contains no rows" });

            var header = parsed[0];
            var warnings = new JArray();
            int maxColumns = parsed.Max(r => r.Count);
            if (maxColumns != header.Count)
                warnings.Add($"Some rows have a different column count than the header ({header.Count}); padded with nulls");

            var columns = new JArray(header.Select(h => h.Trim()));
            var rows = new JArray();
            foreach (var row in parsed.Skip(1))
            {
                if (row.All(f => f.Length == 0) && row.Count <= 1) continue; // skip blank lines

                var obj = new JObject();
                for (int c = 0; c < header.Count; c++)
                    obj[header[c].Trim()] = c < row.Count ? CoerceValue(row[c]) : JValue.CreateNull();
                rows.Add(obj);
            }

            return Ok(new JObject
            {
                ["source"] = source,
                ["delimiter"] = delimiter.ToString(),
                ["columns"] = columns,
                ["columnCount"] = header.Count,
                ["rowCount"] = rows.Count,
                ["rows"] = rows,
                ["warnings"] = warnings
            });

            static char? ParseDelimiter(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return ',';
                var trimmed = value.Trim();
                if (trimmed.Length != 1 || char.IsWhiteSpace(trimmed[0])) return null;
                return trimmed[0];
            }

            static JToken CoerceValue(string field)
            {
                string f = field.Trim();
                if (f.Length == 0) return JValue.CreateNull();
                if (bool.TryParse(f, out var b)) return new JValue(b);
                if (long.TryParse(f, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return new JValue(l);
                if (double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return new JValue(d);
                return new JValue(f);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>FNV-1a 32-bit hash — stable seed for per-city weather.</summary>
        private static uint Fnv1a(string s)
        {
            const uint prime = 16777619;
            uint hash = 2166136261;
            foreach (var c in s)
            {
                hash ^= c;
                hash *= prime;
            }
            return hash;
        }

        /// <summary>
        /// Minimal RFC-4180 CSV parser: quoted fields, embedded delimiters/newlines,
        /// doubled-quote escaping. Returns raw string records (header included).
        /// </summary>
        internal static class CsvParser
        {
            public static List<List<string>> Parse(string text, char delimiter)
            {
                var records = new List<List<string>>();
                var field = new StringBuilder();
                var record = new List<string>();
                bool inQuotes = false;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inQuotes)
                    {
                        if (c == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                            else inQuotes = false;
                        }
                        else field.Append(c);
                    }
                    else if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == delimiter)
                    {
                        record.Add(field.ToString());
                        field.Clear();
                    }
                    else if (c == '\r')
                    {
                        // skip; \n terminates the record
                    }
                    else if (c == '\n')
                    {
                        record.Add(field.ToString());
                        field.Clear();
                        records.Add(record);
                        record = new List<string>();
                    }
                    else
                    {
                        field.Append(c);
                    }
                }

                // Final record without trailing newline.
                if (field.Length > 0 || record.Count > 0)
                {
                    record.Add(field.ToString());
                    records.Add(record);
                }

                return records;
            }
        }
    }
}
