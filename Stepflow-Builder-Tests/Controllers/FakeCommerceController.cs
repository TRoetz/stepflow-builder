using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Stepflow_Builder_Tests.Controllers;

/// <summary>
/// In-memory state backing the fake commerce APIs (inventory, payments, receipts, email outbox).
/// Registered as a singleton so each app instance (dev server or test factory) owns isolated state.
/// </summary>
public sealed class FakeCommerceStore
{
    /// <summary>Stock granted to SKUs that are not in the seed table.</summary>
    public const int DefaultStock = 100;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, int> _physicalStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SKU-001"] = 50,
        ["SKU-002"] = 30,
        ["SKU-003"] = 10,
        // Scarce item - used to exercise the insufficient-stock path.
        ["SKU-999"] = 1
    };

    public sealed class Reservation
    {
        public string CartId { get; init; } = "";
        public JArray Items { get; init; } = new();
        public DateTime ReservedAtUtc { get; init; }
    }

    private readonly ConcurrentDictionary<string, Reservation> _reservations = new(StringComparer.OrdinalIgnoreCase);

    public sealed class SentEmail
    {
        public string MessageId { get; init; } = "";
        public string To { get; init; } = "";
        public string Subject { get; init; } = "";
        public string BodyText { get; init; } = "";
        public JArray Attachments { get; init; } = new();
    }

    private readonly List<SentEmail> _outbox = new();
    private int _counter;

    /// <summary>Next deterministic id for a prefix (TXN-, RES-, RCPT-, MSG-, PICKUP-).</summary>
    public string NextId(string prefix) => $"{prefix}{Interlocked.Increment(ref _counter):D6}";

    public int PhysicalAvailable(string sku) =>
        _physicalStock.TryGetValue(sku, out var n) ? n : DefaultStock;

    /// <summary>Physical stock minus everything currently blocked by open reservations.</summary>
    public int EffectiveAvailable(string sku)
    {
        lock (_gate)
        {
            var reserved = 0;
            foreach (var r in _reservations.Values)
                foreach (var item in r.Items)
                    if (string.Equals((string?)item["sku"], sku, StringComparison.OrdinalIgnoreCase))
                        reserved += (int)(item["qty"] ?? 0);
            return PhysicalAvailable(sku) - reserved;
        }
    }

    /// <summary>Per-item availability check. Pure read - never mutates state.</summary>
    public JObject CheckItems(string cartId, JArray items)
    {
        var result = new JArray();
        var shortages = new JArray();
        var ok = true;
        foreach (var item in items)
        {
            var sku = (string?)item["sku"] ?? "";
            var requested = (int)(item["qty"] ?? 0);
            var available = EffectiveAvailable(sku);
            var itemOk = available >= requested;
            if (!itemOk) ok = false;
            result.Add(new JObject
            {
                ["sku"] = sku,
                ["requested"] = requested,
                ["available"] = available,
                ["ok"] = itemOk
            });
            if (!itemOk) shortages.Add(sku);
        }
        return new JObject
        {
            ["cartId"] = cartId,
            ["ok"] = ok,
            ["items"] = result,
            ["shortages"] = shortages
        };
    }

    /// <summary>Blocks stock for a cart. Returns null (with the check payload) when any item is short.</summary>
    public (bool Ok, string? ReservationId, JObject Check) Reserve(string cartId, JArray items)
    {
        lock (_gate)
        {
            var check = CheckItems(cartId, items);
            if ((bool?)check["ok"] != true) return (false, null, check);
            var id = NextId("RES-");
            _reservations[id] = new Reservation
            {
                CartId = cartId,
                Items = (JArray)items.DeepClone(),
                ReservedAtUtc = DateTime.UtcNow
            };
            return (true, id, check);
        }
    }

    /// <summary>Releases a blocked reservation (cart cleared/abandoned). False when unknown.</summary>
    public bool Release(string reservationId)
    {
        lock (_gate)
        {
            if (!_reservations.TryRemove(reservationId, out _)) return false;
            return true;
        }
    }

    /// <summary>Converts a blocked reservation into a real deduction. False when unknown/already consumed.</summary>
    public bool Deduct(string reservationId)
    {
        lock (_gate)
        {
            if (!_reservations.TryRemove(reservationId, out var r)) return false;
            foreach (var item in r.Items)
            {
                var sku = (string?)item["sku"] ?? "";
                _physicalStock.AddOrUpdate(sku, DefaultStock - (int)(item["qty"] ?? 0), (_, cur) => cur - (int)(item["qty"] ?? 0));
            }
            return true;
        }
    }

    public bool HasReservation(string reservationId) => _reservations.ContainsKey(reservationId);

    /// <summary>Charges a card. Deterministic decline rules: last4 "0002" = insufficient funds, &gt;$50k = over limit.</summary>
    public JObject Charge(JObject request)
    {
        var orderId = (string?)request["orderId"] ?? "";
        var amountCents = (long)(request["amountCents"] ?? 0);
        if ((string?)request["cardLast4"] == "0002")
            return new JObject { ["status"] = "declined", ["orderId"] = orderId, ["declineReason"] = "insufficient_funds" };
        if (amountCents > 5_000_000)
            return new JObject { ["status"] = "declined", ["orderId"] = orderId, ["declineReason"] = "exceeds_card_limit" };
        return new JObject
        {
            ["status"] = "succeeded",
            ["transactionId"] = NextId("TXN-"),
            ["orderId"] = orderId,
            ["amountCents"] = amountCents,
            ["currency"] = (string?)request["currency"] ?? "USD"
        };
    }

    public JObject CreateReceipt(JObject request)
    {
        var orderId = (string?)request["orderId"] ?? "";
        var customer = request["customer"] as JObject;
        var items = request["items"] as JArray ?? new JArray();
        var lines = new List<string>
        {
            $"Transaction {(string?)request["transactionId"]}",
            $"Customer: {(string?)customer?["name"]} <{(string?)customer?["email"]}>",
            "----------------------------------------"
        };
        foreach (var item in items)
            lines.Add($"{(string?)item["sku"]} x{item["qty"]}  ${((long)(item["unitPriceCents"] ?? 0)) / 100.0:F2}");
        lines.Add("----------------------------------------");
        lines.Add($"Total {(string?)request["currency"] ?? "USD"}: ${((long)(request["totalCents"] ?? 0)) / 100.0:F2}");

        var pdf = PdfWriter.Build($"RECEIPT {orderId}", lines);
        return new JObject
        {
            ["receiptId"] = NextId("RCPT-"),
            ["fileName"] = $"receipt-{orderId}.pdf",
            ["contentType"] = "application/pdf",
            ["sizeBytes"] = pdf.Length,
            ["pdfBase64"] = Convert.ToBase64String(pdf)
        };
    }

    public JObject SendEmail(JObject request)
    {
        var email = new SentEmail
        {
            MessageId = NextId("MSG-"),
            To = (string?)request["to"] ?? "",
            Subject = (string?)request["subject"] ?? "",
            BodyText = (string?)request["bodyText"] ?? "",
            Attachments = request["attachments"] as JArray ?? new JArray()
        };
        lock (_outbox) _outbox.Add(email);
        return new JObject { ["messageId"] = email.MessageId, ["delivered"] = true, ["attachmentCount"] = email.Attachments.Count };
    }

    public List<SentEmail> Outbox(string? to)
    {
        lock (_outbox)
            return string.IsNullOrEmpty(to)
                ? _outbox.ToList()
                : _outbox.Where(e => e.To.Equals(to, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>Minimal single-page PDF writer - enough for a valid, parseable receipt document.</summary>
internal static class PdfWriter
{
    public static byte[] Build(string title, IEnumerable<string> lines)
    {
        var content = new StringBuilder();
        content.Append("BT /F1 12 Tf 72 720 Td (").Append(Esc(title)).Append(") Tj\n");
        foreach (var line in lines)
            content.Append("0 -16 Td (").Append(Esc(line)).Append(") Tj\n");
        content.Append("ET");
        var contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        using var pdf = new MemoryStream();
        void W(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            pdf.Write(b, 0, b.Length);
        }

        var offsets = new List<long>();
        W("%PDF-1.4\n");
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Position);
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        offsets.Add(pdf.Position);
        W($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        pdf.Write(contentBytes, 0, contentBytes.Length);
        W("\nendstream\nendobj\n");

        var xrefPos = pdf.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
            W($"{off:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static string Esc(string s)
    {
        var t = s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace('\r', ' ').Replace('\n', ' ');
        var chars = new char[t.Length];
        for (var i = 0; i < t.Length; i++) chars[i] = t[i] >= 32 && t[i] < 127 ? t[i] : '?';
        return new string(chars);
    }
}

/// <summary>
/// Fake commerce endpoints for flow testing: inventory check/reserve/release/deduct, card payments,
/// PDF receipts, an observable email outbox and carrier pickup scheduling. All state is in-memory
/// (see <see cref="FakeCommerceStore"/>); observation endpoints let tests assert on side effects.
/// </summary>
[ApiController]
public class FakeCommerceController : ControllerBase
{
    private static readonly HashSet<string> KnownCarriers = new(StringComparer.OrdinalIgnoreCase) { "NZPost", "CourierPost", "Aramex" };

    private readonly FakeCommerceStore _store;
    public FakeCommerceController(FakeCommerceStore store) => _store = store;

    // ── Inventory ────────────────────────────────────────────────────────────────

    /// <summary>Observation endpoint: current availability for one SKU.</summary>
    [HttpGet("api/fake/commerce/inventory")]
    public IActionResult GetInventory([FromQuery] string sku) => Ok(new
    {
        sku,
        available = _store.EffectiveAvailable(sku),
        physical = _store.PhysicalAvailable(sku)
    });

    /// <summary>Per-item availability check for a whole cart. Pure read - blocks nothing.</summary>
    [HttpPost("api/fake/commerce/inventory/check")]
    public IActionResult CheckInventory([FromBody] JObject body) =>
        Ok(_store.CheckItems((string?)body["cartId"] ?? "", body["items"] as JArray ?? new JArray()));

    /// <summary>Blocks stock for a cart until it is cleared (release) or converted to an order (deduct).</summary>
    [HttpPost("api/fake/commerce/inventory/reserve")]
    public IActionResult Reserve([FromBody] JObject body)
    {
        var (ok, id, check) = _store.Reserve((string?)body["cartId"] ?? "", body["items"] as JArray ?? new JArray());
        if (!ok) return Conflict(check);
        return Ok(new { ok = true, reservationId = id, reservedAtUtc = DateTime.UtcNow.ToString("o"), items = check["items"] });
    }

    /// <summary>Releases a blocked reservation (cart cleared/abandoned).</summary>
    [HttpPost("api/fake/commerce/inventory/release")]
    public IActionResult Release([FromBody] JObject body)
    {
        var id = (string?)body["reservationId"] ?? "";
        if (!_store.Release(id)) return NotFound(new { error = "Unknown reservation", reservationId = id });
        return Ok(new { released = true, reservationId = id });
    }

    /// <summary>Converts a blocked reservation into a real stock deduction (order placed).</summary>
    [HttpPost("api/fake/commerce/inventory/deduct")]
    public IActionResult Deduct([FromBody] JObject body)
    {
        var id = (string?)body["reservationId"] ?? "";
        if (!_store.Deduct(id)) return Conflict(new { error = "Reservation unknown or already consumed", reservationId = id });
        return Ok(new { deducted = true, orderId = (string?)body["orderId"], reservationId = id });
    }

    // ── Payments ─────────────────────────────────────────────────────────────────

    /// <summary>Charges a card. Deterministic: last4 "0002" declines with insufficient_funds; &gt;$50k exceeds_card_limit.</summary>
    [HttpPost("api/fake/payments/charge")]
    public IActionResult Charge([FromBody] JObject body) => Ok(_store.Charge(body));

    // ── Receipts (PDF) ───────────────────────────────────────────────────────────

    /// <summary>Renders a PDF receipt for an order and returns it base64-encoded.</summary>
    [HttpPost("api/fake/receipts")]
    public IActionResult CreateReceipt([FromBody] JObject body) => Ok(_store.CreateReceipt(body));

    // ── Email (observable outbox) ────────────────────────────────────────────────

    /// <summary>"Sends" an email into the in-memory outbox.</summary>
    [HttpPost("api/fake/email/send")]
    public IActionResult SendEmail([FromBody] JObject body) => Ok(_store.SendEmail(body));

    /// <summary>Observation endpoint: messages sent so far, optionally filtered by recipient.</summary>
    [HttpGet("api/fake/email/outbox")]
    public IActionResult Outbox([FromQuery] string? to)
    {
        var messages = _store.Outbox(to).Select(e => new JObject
        {
            ["messageId"] = e.MessageId,
            ["to"] = e.To,
            ["subject"] = e.Subject,
            ["bodyText"] = e.BodyText,
            ["attachments"] = e.Attachments
        }).ToList();
        return Ok(new { count = messages.Count, messages });
    }

    // ── Carriers ─────────────────────────────────────────────────────────────────

    /// <summary>Schedules a pickup with the given carrier. Rejects unknown carriers.</summary>
    [HttpPost("api/fake/carriers/pickup")]
    public IActionResult SchedulePickup([FromBody] JObject body)
    {
        var carrier = (string?)body["carrier"] ?? "";
        if (!KnownCarriers.Contains(carrier))
            return BadRequest(new { error = $"Unknown carrier '{carrier}'", known = KnownCarriers.OrderBy(c => c).ToArray() });

        var serviceLevel = (string?)body["serviceLevel"] ?? "standard";
        var daysAhead = serviceLevel switch { "express" => 1, "overnight" => 0, _ => 2 };
        return Ok(new
        {
            pickupId = _store.NextId("PICKUP-"),
            trackingNumber = $"TRK-{(string?)body["orderId"]}-{carrier}",
            carrier,
            serviceLevel,
            scheduledFor = DateTime.UtcNow.AddDays(daysAhead).ToString("yyyy-MM-dd"),
            address = body["address"] ?? new JObject()
        });
    }
}
