using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests;

/// <summary>
/// End-to-end tests for the cust-order-fufillment scenario: cart → (payment sub-flow) → order placed → fulfillment sub-flow,
/// plus the failure paths (insufficient stock, declined payment, abandoned cart).
/// Two hosts run per class: the main app on an in-memory TestServer (registration/execution - flow:// references resolve
/// in-process via StepFunctionService) and the Stepflow-Builder-Tests fake API host on a real Kestrel ephemeral port (the
/// commerce/payment/email endpoints the flows call over HTTP through the engine's IHttpClientFactory). No fixed ports, so
/// test classes run in parallel.
/// </summary>
public sealed class CustOrderFulfillmentScenarioTests : IClassFixture<CustOrderFulfillmentScenarioTests.Factory>, IClassFixture<CustOrderFulfillmentScenarioTests.FakeFactory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        // Isolate this host's durable flow-state store in a fresh temp dir so startup recovery never loads
        // checkpoints from other runs (their embedded definitions carry stale ephemeral ports).
        private readonly string _flowStateDir = Path.Combine(Path.GetTempPath(), "stepflow-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { [FlowStateOptions.SectionName + ":DiskPath"] = _flowStateDir }));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { Directory.Delete(_flowStateDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Fake test API host (Stepflow-Builder-Tests) on a real Kestrel socket - the scenario flows' commerce/payment/email resources live here.</summary>
    public sealed class FakeFactory : WebApplicationFactory<Stepflow_Builder_Tests.Program>
    {
        private bool _started;

        public string BaseUrl { get; private set; } = "";

        /// <summary>Binds an ephemeral loopback port and captures it. Idempotent - safe to call from every test constructor.</summary>
        public void StartKestrel()
        {
            if (_started) return;
            _started = true;
            UseKestrel(o => o.Listen(IPAddress.Loopback, 0));
            _ = CreateClient(); // start the server so its address is available
            var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            BaseUrl = (addresses.FirstOrDefault(a => a.Contains("127.0.0.1")) ?? addresses.First()).TrimEnd('/');
        }
    }

    private readonly HttpClient _client;       // main app: flow registration + execution
    private readonly HttpClient _fakeClient;   // fake host: commerce inventory + email outbox observation
    private readonly string _fakeBaseUrl;      // the flows' HTTP resources point here (rewritten from localhost:5095)
    private readonly string _flowsDir;

    public CustOrderFulfillmentScenarioTests(Factory factory, FakeFactory fakeFactory)
    {
        if (string.IsNullOrEmpty(fakeFactory.BaseUrl)) fakeFactory.StartKestrel();
        _client = factory.CreateClient();
        _fakeClient = fakeFactory.CreateClient();
        _fakeBaseUrl = fakeFactory.BaseUrl;
        Assert.StartsWith("http://", _fakeBaseUrl);
        _flowsDir = Path.Combine(AppContext.BaseDirectory, "flow-scenarios", "cust-order-fufillment", "flows");
    }

    [Fact]
    public async Task FullJourney_StandardDelivery_PaysReceiptsEmailsAndFulfills()
    {
        await SetupAsync();

        var baseline001 = await GetAvailableAsync("SKU-001");
        var cartOut = await RunFlowAsync("CartInventoryCheck", CartInput("CART-1", "jane@example.com", ("SKU-001", 2, 1999), ("SKU-002", 1, 4500)));
        Assert.True((string)cartOut["status"] == "Succeeded", cartOut.ToString(Formatting.None));
        var state = cartOut["output"]!;
        Assert.True((bool)state["inventoryCheck"]["ok"]);
        Assert.StartsWith("RES-", (string)state["reservation"]["reservationId"]);
        Assert.Equal(8498, (long)state["totals"]["rows"][0]["total_cents"]);

        var orderInput = state.DeepClone();
        orderInput["orderId"] = "ORD-777";
        orderInput["cardLast4"] = "4242";
        orderInput["currency"] = "USD";
        orderInput["totalCents"] = (long)state["totals"]["rows"][0]["total_cents"];
        orderInput["deliveryMethod"] = "standard";
        orderInput["shippingAddress"] = new JObject { ["street"] = "42 Lambton Quay", ["city"] = "Wellington", ["region"] = "WGN", ["postalCode"] = "6011" };

        var placedOut = await RunFlowAsync("OrderPlaced", orderInput);
        Assert.Equal("Succeeded", (string)placedOut["status"]);
        var final = placedOut["output"]!;

        // Payment sub-flow results.
        Assert.Equal("succeeded", (string)final["payment"]["status"]);
        Assert.StartsWith("TXN-", (string)final["payment"]["transactionId"]);

        // PDF receipt: valid document, named after the order, containing it.
        var pdf = Convert.FromBase64String((string)final["receipt"]["pdfBase64"]);
        var text = Encoding.ASCII.GetString(pdf);
        Assert.Equal("receipt-ORD-777.pdf", (string)final["receipt"]["fileName"]);
        Assert.StartsWith("%PDF-", text);
        Assert.EndsWith("%%EOF", text.TrimEnd());
        Assert.Contains("ORD-777", text);

        // Email sub-flow result + observable outbox side effect.
        Assert.True((bool)final["email"]["delivered"]);
        var outbox = await GetOutboxAsync("jane@example.com");
        Assert.Equal(1, (int)outbox["count"]);
        var attachment = outbox["messages"][0]["attachments"][0]!;
        Assert.Equal("receipt-ORD-777.pdf", (string)attachment["name"]);
        Assert.Equal("application/pdf", (string)attachment["contentType"]);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(Convert.FromBase64String((string)attachment["contentBase64"])));

        // Stock was deducted from the reservation.
        Assert.True((bool)final["inventory"]["deducted"]);
        Assert.Equal(baseline001 - 2, await GetAvailableAsync("SKU-001"));

        // Fulfillment sub-flow: standard -> NZPost pickup.
        Assert.Equal("NZPost", (string)final["fulfillment"]["carrier"]);
        Assert.Equal("standard", (string)final["fulfillment"]["serviceLevel"]);
        Assert.StartsWith("PICKUP-", (string)final["fulfillment"]["pickup"]["pickupId"]);
        Assert.Equal("TRK-ORD-777-NZPost", (string)final["fulfillment"]["pickup"]["trackingNumber"]);
    }

    [Fact]
    public async Task FullJourney_OvernightDelivery_RoutesToAramex()
    {
        await SetupAsync();

        var cartOut = await RunFlowAsync("CartInventoryCheck", CartInput("CART-2", "jane-night@example.com", ("SKU-003", 1, 999)));
        Assert.Equal("Succeeded", (string)cartOut["status"]);

        var orderInput = cartOut["output"]!.DeepClone();
        orderInput["orderId"] = "ORD-888";
        orderInput["cardLast4"] = "4242";
        orderInput["currency"] = "USD";
        orderInput["totalCents"] = (long)cartOut["output"]["totals"]["rows"][0]["total_cents"];
        orderInput["deliveryMethod"] = "overnight";
        orderInput["shippingAddress"] = new JObject { ["street"] = "1 Featherbed St", ["city"] = "Auckland", ["region"] = "AKL", ["postalCode"] = "1010" };

        var placedOut = await RunFlowAsync("OrderPlaced", orderInput);
        Assert.Equal("Succeeded", (string)placedOut["status"]);
        var final = placedOut["output"]!;

        Assert.Equal("Aramex", (string)final["fulfillment"]["carrier"]);
        Assert.Equal("overnight", (string)final["fulfillment"]["serviceLevel"]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", (string)final["fulfillment"]["pickup"]["scheduledFor"]);
        Assert.Equal("TRK-ORD-888-Aramex", (string)final["fulfillment"]["pickup"]["trackingNumber"]);
    }

    [Fact]
    public async Task InsufficientStock_FailsCartWithoutBlockingAnything()
    {
        await SetupAsync();

        var before = await GetAvailableAsync("SKU-999"); // seed: 1 unit
        var outboxBefore = (int)(await GetOutboxAsync("jane-short@example.com"))["count"];

        var result = await RunFlowAsync("CartInventoryCheck", CartInput("CART-SHORT", "jane-short@example.com", ("SKU-999", 5, 100)));

        Assert.Equal("Failed", (string)result["status"]);
        Assert.Equal("InsufficientStock", (string)result["errorCode"]);
        // Nothing was blocked: availability is unchanged and no reservation exists.
        Assert.Equal(before, await GetAvailableAsync("SKU-999"));
        Assert.Equal(outboxBefore, (int)(await GetOutboxAsync("jane-short@example.com"))["count"]);
    }

    [Fact]
    public async Task DeclinedPayment_FailsOrderAndReleasesBlockedStock()
    {
        await SetupAsync();

        var baseline = await GetAvailableAsync("SKU-003");
        var cartOut = await RunFlowAsync("CartInventoryCheck", CartInput("CART-4", "jane-declined@example.com", ("SKU-003", 2, 750)));
        Assert.Equal("Succeeded", (string)cartOut["status"]);

        // Stock is blocked while the cart is open.
        Assert.Equal(baseline - 2, await GetAvailableAsync("SKU-003"));

        var orderInput = cartOut["output"]!.DeepClone();
        orderInput["orderId"] = "ORD-999";
        orderInput["cardLast4"] = "0002"; // deterministic decline: insufficient_funds
        orderInput["currency"] = "USD";
        orderInput["totalCents"] = (long)cartOut["output"]["totals"]["rows"][0]["total_cents"];
        orderInput["deliveryMethod"] = "standard";
        orderInput["shippingAddress"] = new JObject { ["street"] = "9 Cuba St", ["city"] = "Wellington", ["region"] = "WGN", ["postalCode"] = "6011" };

        var result = await RunFlowAsync("OrderPlaced", orderInput);

        Assert.Equal("Failed", (string)result["status"]);
        Assert.Equal("OrderPaymentFailed", (string)result["errorCode"]);
        // The catch path released the blocked stock.
        Assert.Equal(baseline, await GetAvailableAsync("SKU-003"));
        // No receipt was ever emailed for this order.
        Assert.Equal(0, (int)(await GetOutboxAsync("jane-declined@example.com"))["count"]);
    }

    [Fact]
    public async Task AbandonedCart_ClearsReservationAndRestoresStock()
    {
        await SetupAsync();

        var baseline = await GetAvailableAsync("SKU-002");
        var cartOut = await RunFlowAsync("CartInventoryCheck", CartInput("CART-ABANDON", "jane-abandon@example.com", ("SKU-002", 1, 4500)));
        Assert.Equal("Succeeded", (string)cartOut["status"]);
        var reservationId = (string)cartOut["output"]!["reservation"]["reservationId"];

        // Blocked while the cart is open.
        Assert.Equal(baseline - 1, await GetAvailableAsync("SKU-002"));

        var cleared = await RunFlowAsync("CartCleared", new JObject { ["reservationId"] = reservationId });

        Assert.Equal("Succeeded", (string)cleared["status"]);
        Assert.True((bool)cleared["output"]!["release"]["released"]);
        // Stock is available to other carts again.
        Assert.Equal(baseline, await GetAvailableAsync("SKU-002"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task SetupAsync() => await Task.WhenAll(
        RegisterFlowAsync("cart-inventory-check.json"),
        RegisterFlowAsync("cart-cleared.json"),
        RegisterFlowAsync("payment-flow.json"),
        RegisterFlowAsync("order-placed.json"),
        RegisterFlowAsync("order-fulfillment.json"));

    /// <summary>Registers a scenario flow with its stable logical id (top-level "Id") so flow:// references resolve by name.</summary>
    private async Task<string> RegisterFlowAsync(string fileName)
    {
        var raw = await File.ReadAllTextAsync(Path.Combine(_flowsDir, fileName));
        raw = raw.Replace("http://localhost:5095", _fakeBaseUrl); // point the flows' commerce endpoints at the fake test API host
        var def = JObject.Parse(raw);

        var payload = new JObject
        {
            ["name"] = (string?)def["Id"],
            ["id"] = (string?)def["Id"],
            ["description"] = (string?)def["Comment"],
            ["startAt"] = (string?)def["StartAt"],
            ["states"] = def["States"]!
        };

        var resp = await _client.PostAsync("/api/flows", JsonContent(payload));
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Registering {fileName} failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
        return (string)(JObject.Parse(await resp.Content.ReadAsStringAsync()))["id"]!;
    }

    private async Task<JObject> RunFlowAsync(string flowId, JToken input)
    {
        var resp = await _client.PostAsync($"/api/flows/execute-sync/{flowId}", JsonContent(input));
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Executing {flowId} failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
        return JObject.Parse(await resp.Content.ReadAsStringAsync());
    }

    private async Task<int> GetAvailableAsync(string sku)
    {
        var resp = await _fakeClient.GetAsync($"/api/fake/commerce/inventory?sku={Uri.EscapeDataString(sku)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (int)(JObject.Parse(await resp.Content.ReadAsStringAsync()))["available"]!;
    }

    private async Task<JObject> GetOutboxAsync(string to)
    {
        var resp = await _fakeClient.GetAsync($"/api/fake/email/outbox?to={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JObject.Parse(await resp.Content.ReadAsStringAsync());
    }

    private static StringContent JsonContent(JToken token) => new(token.ToString(Formatting.None), Encoding.UTF8, "application/json");

    private static JObject CartInput(string cartId, string email, params (string sku, int qty, int priceCents)[] items) => new()
    {
        ["cartId"] = cartId,
        ["customer"] = new JObject { ["id"] = "CUST-42", ["name"] = "Jane Doe", ["email"] = email },
        ["items"] = new JArray(items.Select(i => (JToken)new JObject
        {
            ["sku"] = i.sku,
            ["qty"] = i.qty,
            ["unitPriceCents"] = i.priceCents
        }))
    };
}
