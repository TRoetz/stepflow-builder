using System.Globalization;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// Direct API tests for the fake data endpoints (Stepflow-Builder-Tests/Controllers/FakeDataController.cs).
/// Every response is deterministic - values derive from request parameters, never the wall clock.
/// </summary>
public sealed class FakeDataApiTests : IClassFixture<FakeDataApiTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Stepflow_Builder_Tests.Program> { }

    private readonly HttpClient _client;

    public FakeDataApiTests(Factory factory) => _client = factory.CreateClient();

    // Parse with date strings left as strings - default JObject.Parse converts ISO dates to JValue(DateTime), which breaks string assertions via culture-dependent ToString().
    private static JObject ParseJson(string text)
    {
        using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    // ── Weather ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Weather_ReturnsDeterministicReport()
    {
        var w = ParseJson(await _client.GetStringAsync("/api/fake/weather?city=Wellington&units=celsius"));

        Assert.Equal("Wellington", (string)w["city"]);
        Assert.Equal("celsius", (string)w["units"]);
        Assert.InRange((double)w["temperature"], -5, 38);
        Assert.InRange((int)w["humidityPercent"], 30, 90);
        Assert.Equal("2026-08-19T06:30:00Z", (string)w["observedAt"]);

        var forecast = (JArray)w["forecast"];
        Assert.Equal(5, forecast.Count);
        Assert.Equal("today", (string)forecast[0]["day"]);
        for (var i = 1; i < 5; i++)
            Assert.Equal($"day-{i}", (string)forecast[i]["day"]);
    }

    [Fact]
    public async Task Weather_SameCityIsIdenticalAcrossCalls()
    {
        var a = await _client.GetStringAsync("/api/fake/weather?city=Auckland&units=celsius");
        var b = await _client.GetStringAsync("/api/fake/weather?city=Auckland&units=celsius");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Weather_FahrenheitMatchesCelsiusConversion()
    {
        var c = ParseJson(await _client.GetStringAsync("/api/fake/weather?city=Wellington&units=celsius"));
        var f = ParseJson(await _client.GetStringAsync("/api/fake/weather?city=Wellington&units=fahrenheit"));

        Assert.Equal("fahrenheit", (string)f["units"]);
        // Same underlying celsius value, converted with the same rounding.
        Assert.Equal(Math.Round((double)c["temperature"] * 9.0 / 5.0 + 32.0, 1), (double)f["temperature"]);
    }

    [Fact]
    public async Task Weather_MissingCity_Returns400()
    {
        var resp = await _client.GetAsync("/api/fake/weather");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("city", await resp.Content.ReadAsStringAsync());
    }

    // ── Gold price ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GoldPrice_DefaultUsdValues()
    {
        var g = ParseJson(await _client.GetStringAsync("/api/fake/gold-price"));

        Assert.Equal("gold", (string)g["metal"]);
        Assert.Equal("24k (999.9)", (string)g["purity"]);
        Assert.Equal("troy_ounce", (string)g["unit"]);
        Assert.Equal("USD", (string)g["currency"]);
        Assert.Equal(1, (double)g["amount"]);
        Assert.Equal(2650.42, (double)g["pricePerOunce"]);
        Assert.Equal(2650.42, (double)g["totalValue"]);
        Assert.Equal(0.84, (double)g["changePercent24h"]);
    }

    [Fact]
    public async Task GoldPrice_NzdTwoOunces()
    {
        var g = ParseJson(await _client.GetStringAsync("/api/fake/gold-price?currency=NZD&amount=2"));

        Assert.Equal("NZD", (string)g["currency"]);
        Assert.Equal(4521.75, (double)g["pricePerOunce"]);
        Assert.Equal(Math.Round(4521.75 * 2, 2), (double)g["totalValue"]); // 9043.5
    }

    [Fact]
    public async Task GoldPrice_UnsupportedCurrency_Returns400WithSupportedList()
    {
        var resp = await _client.GetAsync("/api/fake/gold-price?currency=XAU");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = ParseJson(await resp.Content.ReadAsStringAsync());
        Assert.Contains("XAU", (string)body["error"]);
        var supported = (JArray)body["supported"];
        Assert.Contains("USD", supported.Select(t => (string)t));
        Assert.Contains("NZD", supported.Select(t => (string)t));
    }

    // ── Exchange rate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExchangeRate_UsdToNzd()
    {
        var r = ParseJson(await _client.GetStringAsync("/api/fake/exchange-rate?from=USD&to=NZD&amount=100"));

        Assert.Equal("USD", (string)r["from"]);
        Assert.Equal("NZD", (string)r["to"]);
        Assert.Equal(100, (double)r["amount"]);
        Assert.Equal(Math.Round(1.6327, 6), (double)r["baseRate"]);
        Assert.Equal(Math.Round(100 * 1.6327, 4), (double)r["convertedAmount"]); // 163.27
        Assert.Equal("2026-08-19T00:00:00Z", (string)r["timestamp"]);
    }

    [Fact]
    public async Task ExchangeRate_NzdToUsdIsDerivedCrossRate()
    {
        var r = ParseJson(await _client.GetStringAsync("/api/fake/exchange-rate?from=NZD&to=USD"));

        // Cross rate derived from the per-USD table: 1 / NZD-per-USD.
        var rate = 1.0 / 1.6327;
        Assert.Equal(Math.Round(rate, 6), (double)r["baseRate"]);
        Assert.Equal(Math.Round(1 * rate, 4), (double)r["convertedAmount"]);
    }

    [Fact]
    public async Task ExchangeRate_MissingParams_Returns400()
    {
        var resp = await _client.GetAsync("/api/fake/exchange-rate?from=USD");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Large-volume JSON dataset ──────────────────────────────────────────────

    [Fact]
    public async Task JsonRecords_CountAndShape()
    {
        var d = ParseJson(await _client.GetStringAsync("/api/fake/json/records?count=5&seed=9"));

        Assert.Equal("fake-data-api", (string)d["source"]);
        Assert.Equal(9, (int)d["seed"]);
        Assert.Equal(5, (int)d["requested"]);
        Assert.Equal(5, (int)d["count"]);
        Assert.False((bool?)d["clampedToMax"] ?? false);

        var regions = new[] { "APAC", "EMEA", "AMER", "LATAM" };
        var records = (JArray)d["records"];
        for (var i = 0; i < 5; i++)
        {
            var rec = records[i];
            Assert.Equal($"REC-0009-{i + 1:D6}", (string)rec["id"]);
            Assert.False(string.IsNullOrEmpty((string)rec["name"]));
            Assert.Contains((string)rec["region"], regions);
            Assert.InRange((double)rec["salary"], 35_000, 130_000);

            var tags = (JArray)rec["tags"];
            Assert.InRange(tags.Count, 1, 4);

            var orders = (JArray)rec["orders"];
            Assert.InRange(orders.Count, 1, 3);
            foreach (var order in orders)
                Assert.InRange(((JArray)order["items"]).Count, 1, 3);
        }
    }

    [Fact]
    public async Task JsonRecords_ClampsToMaxWithFlag()
    {
        var d = ParseJson(await _client.GetStringAsync("/api/fake/json/records?count=50000&seed=1"));

        Assert.True((bool)d["clampedToMax"]);
        Assert.Equal(50_000, (int)d["requested"]);
        Assert.Equal(10_000, (int)d["count"]);
        Assert.Equal(10_000, ((JArray)d["records"]).Count);
    }

    [Fact]
    public async Task JsonRecords_DeterministicForSeed()
    {
        var a = await _client.GetStringAsync("/api/fake/json/records?count=50&seed=42");
        var b = await _client.GetStringAsync("/api/fake/json/records?count=50&seed=42");
        Assert.Equal(a, b);
    }

    // ── XML invoices ───────────────────────────────────────────────────────────

    [Fact]
    public async Task XmlInvoices_ParsesAndTotalsMatch()
    {
        var resp = await _client.GetAsync("/api/fake/xml/invoices?count=5&seed=7");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("application/xml", resp.Content.Headers.ContentType?.ToString());

        var doc = XDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.Root!;
        Assert.Equal("Invoices", root.Name.LocalName);
        Assert.Equal("USD", (string)root.Attribute("currency"));
        Assert.Equal("5", (string)root.Attribute("count"));

        var invoices = root.Elements().ToList();
        Assert.Equal(5, invoices.Count);
        foreach (var inv in invoices)
        {
            Assert.StartsWith("INV-007-", (string)inv.Attribute("id"));
            Assert.False(string.IsNullOrEmpty((string)inv.Attribute("date")));
            Assert.False(string.IsNullOrEmpty(inv.Element("Customer")?.Value));

            var items = inv.Elements("LineItems").Single().Elements("Item").ToList();
            Assert.InRange(items.Count, 1, 4);

            var sum = items.Sum(it => double.Parse(it.Element("LineTotal")!.Value, CultureInfo.InvariantCulture));
            var total = double.Parse(inv.Element("Total")!.Value, CultureInfo.InvariantCulture);
            Assert.True(Math.Abs(total - sum) < 0.01, $"Invoice {(string)inv.Attribute("id")}: Total {total} != line-item sum {sum}");
        }
    }

    [Fact]
    public async Task XmlInvoices_ClampsTo500()
    {
        var resp = await _client.GetAsync("/api/fake/xml/invoices?count=9999&seed=1");
        var doc = XDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("500", (string)doc.Root!.Attribute("count"));
    }

    // ── CSV sample ("file" download) ───────────────────────────────────────────

    [Fact]
    public async Task CsvSample_HeaderAndRowShape()
    {
        var resp = await _client.GetAsync("/api/fake/csv/sample?rows=3&seed=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("text/csv", resp.Content.Headers.ContentType?.ToString());

        var text = await resp.Content.ReadAsStringAsync();
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        Assert.Equal(4, lines.Count); // header + 3 rows
        Assert.Equal("order_id,customer,region,product,quantity,unit_price,amount,ordered_at", lines[0]);

        foreach (var line in lines.Skip(1))
        {
            var firstComma = line.IndexOf(',');
            Assert.StartsWith("ORD-001-", line[..firstComma]); // seed=1 -> ORD-001-#####
            // Last field is the ISO timestamp; product may be quoted and contain commas.
            var lastComma = line.LastIndexOf(',');
            var ts = line[(lastComma + 1)..];
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00Z$", ts);
        }
    }

    // ── CSV import (the file-reading process) ──────────────────────────────────

    [Fact]
    public async Task CsvImport_JsonBody_CoercesTypes()
    {
        var payload = new StringContent(
            JObject.FromObject(new { csv = "a,b,c\n1,true,2.5", fileName = "types.csv" }).ToString(),
            Encoding.UTF8, "application/json");

        var resp = await _client.PostAsync("/api/fake/csv/import", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var r = ParseJson(await resp.Content.ReadAsStringAsync());
        Assert.Equal("types.csv", (string)r["source"]);
        Assert.Equal(",", (string)r["delimiter"]);
        Assert.Equal(3, (int)r["columnCount"]);
        Assert.Equal(1, (int)r["rowCount"]);

        var row = r["rows"]![0]!;
        Assert.Equal(JTokenType.Integer, row["a"]!.Type);
        Assert.Equal(1, (long)row["a"]);
        Assert.Equal(JTokenType.Boolean, row["b"]!.Type);
        Assert.True((bool)row["b"]);
        Assert.Equal(JTokenType.Float, row["c"]!.Type);
        Assert.Equal(2.5, (double)row["c"]);
    }

    [Fact]
    public async Task CsvImport_RoundTripsSampleFile()
    {
        // Download the sample "file", then re-import it as a raw text/csv body.
        var csv = await _client.GetStringAsync("/api/fake/csv/sample?rows=5&seed=2");

        var payload = new StringContent(csv, Encoding.UTF8, "text/csv");
        var resp = await _client.PostAsync("/api/fake/csv/import", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var r = ParseJson(await resp.Content.ReadAsStringAsync());
        Assert.Equal("raw-body", (string)r["source"]);
        Assert.Equal(5, (int)r["rowCount"]);
        Assert.Equal(8, (int)r["columnCount"]);
        Assert.Empty((JArray)r["warnings"]!);

        var products = new[] { "Widget Pro", "Gadget Mini", "Sensor Array X2", "Cable Kit 5m", "Power Adapter 65W", "Mounting Bracket, Heavy Duty" };
        foreach (var row in r["rows"]!)
        {
            // Quoted product field (may contain a comma) must survive the parse.
            Assert.Contains((string)row["product"], products);
            Assert.Equal(JTokenType.Integer, row["quantity"]!.Type);
            Assert.Equal(JTokenType.Float, row["amount"]!.Type);
        }
    }

    [Fact]
    public async Task CsvImport_MultipartFileUpload()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("x,y\n1,hello\n2,true\n", Encoding.UTF8), "file", "orders.csv");

        var resp = await _client.PostAsync("/api/fake/csv/import", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var r = ParseJson(await resp.Content.ReadAsStringAsync());
        Assert.Equal("orders.csv", (string)r["source"]);
        Assert.Equal(2, (int)r["rowCount"]);
        Assert.Equal(1, (long)r["rows"]![0]!["x"]);
        Assert.Equal("hello", (string)r["rows"][0]["y"]);
        Assert.True((bool)r["rows"]![1]!["y"]); // "true" coerced to boolean
    }

    [Fact]
    public async Task CsvImport_JsonBody_MissingCsvField_Returns400()
    {
        var payload = new StringContent("{}", Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("/api/fake/csv/import", payload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("'csv'", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CsvImport_InvalidJson_Returns400()
    {
        var payload = new StringContent("{not json", Encoding.UTF8, "application/json");
        var resp = await _client.PostAsync("/api/fake/csv/import", payload);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Invalid JSON body", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CsvImport_MultipartWithoutFile_Returns400()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(","), "delimiter");

        var resp = await _client.PostAsync("/api/fake/csv/import", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("file part", await resp.Content.ReadAsStringAsync());
    }
}
