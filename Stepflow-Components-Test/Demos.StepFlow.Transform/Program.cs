using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

Console.WriteLine("+++ StepFlow.Transform demo +++");
Console.WriteLine("In-memory DuckDB: load, introspect, query, filter, project, aggregate\n");

var duck = new DuckDbTransformService(NullLogger<DuckDbTransformService>.Instance);

// ── 1. Load inline JSON data into a named table ───────────────────────────────
var orders = JArray.Parse("""
[
  { "id": 1, "customer": "alice",   "region": "north", "amount": 250 },
  { "id": 2, "customer": "bob",     "region": "south", "amount": 120 },
  { "id": 3, "customer": "carol",   "region": "north", "amount": 480 },
  { "id": 4, "customer": "dave",    "region": "east",  "amount": 95  },
  { "id": 5, "customer": "erin",    "region": "south", "amount": 310 },
  { "id": 6, "customer": "frank",   "region": "north", "amount": 75  }
]
""");
duck.LoadJsonData("orders", orders);

// ── 2. Introspection: tables, schema (DESCRIBE) ──────────────────────────────
Console.WriteLine($"[introspect] tables: {string.Join(", ", duck.ListTables())}");
var schema = duck.GetTableSchema("orders");
foreach (var col in schema)
    Console.WriteLine($"  column: {col["column_name"]} : {col["column_type"]}");

// ── 3. Raw SQL query ─────────────────────────────────────────────────────────
Console.WriteLine("\n[query] SELECT * FROM orders WHERE amount > 200 ORDER BY amount DESC");
foreach (var row in duck.ExecuteQuery("SELECT * FROM orders WHERE amount > 200 ORDER BY amount DESC"))
    Console.WriteLine($"  id={row["id"]} customer={row["customer"]} region={row["region"]} amount={row["amount"]}");
// NOTE: ExecuteQuery(sql, dict) is broken in v1.0.0 — it binds each key as a named parameter "$key",
// which never matches DuckDB's positional ($1) or named (?name) placeholders (verified empirically).

// ── 4. The JObject transform API (what the ASL engine calls) ─────────────────
JObject Run(string label, JObject input)
{
    var result = duck.ExecuteTransform(input);
    Console.WriteLine($"\n[{label}] operation={result["operation"]} success={result["success"]}" +
                      (result["error"] != null ? $" error={result["error"]}" : ""));
    if (result["rows"] is JArray rows)
        foreach (var r in rows) Console.WriteLine($"  {r.ToString(Formatting.None)}");
    return result;
}

Run("filter", new JObject
{
    ["operation"] = "filter",
    ["table"] = "orders",
    ["where"] = "region = 'north'"
});

Run("project", new JObject
{
    ["operation"] = "project",
    ["table"] = "orders",
    ["columns"] = "customer, amount"
});

Run("aggregate", new JObject
{
    ["operation"] = "aggregate",
    ["table"] = "orders",
    ["groupBy"] = "region",
    ["aggregations"] = "COUNT(*) AS orders, SUM(amount) AS total"
});

// Inline data: the transform loads its own table when `data` + `table` are given.
Run("inline-data query", new JObject
{
    ["operation"] = "query",
    ["table"] = "people",
    ["sql"] = "SELECT name, age FROM people WHERE age >= 30 ORDER BY age",
    ["data"] = JArray.Parse("""[{"name":"ann","age":29},{"name":"ben","age":41},{"name":"cyn","age":35}]""")
});

// ── 5. CSV ingestion (string + file) ─────────────────────────────────────────
duck.LoadCsvString("csv_orders", "id,amount\n10,15\n20,25\n");
Console.WriteLine("\n[csv] LoadCsvString -> rows:");
foreach (var row in duck.ExecuteQuery("SELECT * FROM csv_orders ORDER BY id"))
    Console.WriteLine($"  {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}"))}");

var csvPath = Path.Combine(Path.GetTempPath(), "stepflow-demo", "transform.csv");
Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
File.WriteAllText(csvPath, "city,pop\nOslo,700000\nBergen,290000\nTrondheim,210000\n");
duck.LoadCsvFile("cities", csvPath);
Console.WriteLine("\n[csv] LoadCsvFile -> top city:");
foreach (var row in duck.ExecuteQuery("SELECT * FROM cities ORDER BY pop DESC LIMIT 1"))
    Console.WriteLine($"  {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}"))}\n");

Console.WriteLine("Transform demo complete.");
