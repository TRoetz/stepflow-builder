# Scenario: Customer Order Fulfillment (cust-order-fufillment)

A customer adds items to a cart. The system checks inventory and **blocks the stock** while the cart is open, so no other cart can buy it. When the customer checks out, the order is paid through a payment sub-flow (card charge → PDF receipt → email), the blocked stock is converted into a real deduction, and a fulfillment sub-flow applies business rules to pick a carrier and schedule a pickup.

Failure paths are first-class:

- **Insufficient stock** — the cart fails before anything is blocked (`InsufficientStock`).
- **Declined payment** — the order fails, the catch path releases the blocked stock so it is available again (`OrderPaymentFailed`), and no receipt email is sent.
- **Abandoned cart** — a separate flow releases the reservation directly (`cart-cleared`).

## Flow composition

```mermaid
flowchart TD
    A[CartInventoryCheck<br/>check → totals (DuckDB) → reserve] -->|ok| B{checkout?}
    A -->|shortage| X1[Fail: InsufficientStock]
    B -->|abandoned| C[CartCleared<br/>release reservation]
    B -->|placed| D[OrderPlaced]
    D --> E[/PaymentFlow sub-flow/<br/>charge → PDF receipt → email/]
    E -->|succeeded| F[deduct stock]
    F --> G[/OrderFulfillment sub-flow/<br/>delivery method → carrier → pickup/]
    E -->|declined| H[release blocked stock]
    H --> X2[Fail: OrderPaymentFailed]
```

## Flows (`flows/`)

Each flow file carries a top-level `"Id"` — its stable logical id. Tests register flows with that id so `flow://` references resolve by name; the frontend can do the same via the optional `id` field on `POST /api/flows`.

| File | Id | Purpose |
|---|---|---|
| `cart-inventory-check.json` | `CartInventoryCheck` | Check per-item availability, compute total with a DuckDB transform, reserve (block) stock. Fails `InsufficientStock` on shortage. |
| `cart-cleared.json` | `CartCleared` | Release the reservation of an abandoned/cleared cart. |
| `payment-flow.json` | `PaymentFlow` | Sub-flow: charge card → generate PDF receipt → email it to the customer. Fails `PaymentDeclined`. |
| `order-placed.json` | `OrderPlaced` | Calls `flow://PaymentFlow`, deducts stock on success, routes to `flow://OrderFulfillment`. Catches `PaymentDeclined` → releases stock → fails `OrderPaymentFailed`. |
| `order-fulfillment.json` | `OrderFulfillment` | Sub-flow: business rules map delivery method → carrier (standard→NZPost, express→CourierPost, overnight→Aramex) and schedule a pickup. Fails `InvalidDeliveryMethod`. |

## State contracts

### CartInventoryCheck
Input: `{ cartId, customer {id,name,email}, items [{sku, qty, unitPriceCents}] }`
Output (success): input + `inventoryCheck {ok, items[], shortages[]}`, `totals {rows:[{total_cents}], ...}`, `reservation {reservationId, reservedAtUtc, items[]}`

### CartCleared
Input: `{ reservationId }` → Output: `{ release {released} }`

### PaymentFlow (sub-flow)
Input: `{ orderId, totalCents, currency, cardLast4, customer, items[] }` (+ any passthrough fields)
Output (success): input + `payment {status:"succeeded", transactionId}`, `receipt {fileName, contentType, sizeBytes, pdfBase64}`, `email {messageId, delivered, attachmentCount}`

### OrderPlaced
Input: CartInventoryCheck output + `{ orderId, cardLast4, currency, totalCents, deliveryMethod, shippingAddress }`
Output (success): input + `payment`, `receipt`, `email`, `inventory {deducted}`, `fulfillment {carrier, serviceLevel, pickup {pickupId, trackingNumber, scheduledFor}}`

### OrderFulfillment (sub-flow)
Input: full order state incl. `deliveryMethod`, `shippingAddress`
Output: input + `fulfillment {carrier, serviceLevel, pickup {...}}`

## Fake commerce APIs (`Controllers/FakeCommerceController.cs`)

All state lives in the `FakeCommerceStore` singleton — one instance per app process (dev server or test factory), so tests are isolated. Seed stock: `SKU-001`=50, `SKU-002`=30, `SKU-003`=10, `SKU-999`=1 (scarce, for the shortage path); unknown SKUs get 100.

| Endpoint | Purpose |
|---|---|
| `GET /api/fake/commerce/inventory?sku=` | Observation: effective + physical availability. |
| `POST /api/fake/commerce/inventory/check` | Per-item availability for a cart (pure read). |
| `POST /api/fake/commerce/inventory/reserve` | Block stock → `{reservationId}`. 409 with shortages if not fully available. |
| `POST /api/fake/commerce/inventory/release` | Unblock a reservation (cart cleared/abandoned, or payment failed). |
| `POST /api/fake/commerce/inventory/deduct` | Convert a reservation into a real deduction (order placed). 409 if unknown/consumed. |
| `POST /api/fake/payments/charge` | Deterministic card charge: last4 `0002` → declined `insufficient_funds`; >$50,000 → `exceeds_card_limit`. |
| `POST /api/fake/receipts` | Render a real (minimal but valid) PDF receipt; returns base64. |
| `POST /api/fake/email/send` | "Send" an email into the in-memory outbox. |
| `GET /api/fake/email/outbox?to=` | Observation: sent messages incl. attachment bytes — lets tests verify the emailed PDF end-to-end. |
| `POST /api/fake/carriers/pickup` | Schedule a pickup; rejects unknown carriers. standard=+2d, express=+1d, overnight=same day. |

## Running the tests

```powershell
dotnet test --filter CustOrderFulfillmentScenarioTests
```

The fixture (`CustOrderFulfillmentScenarioTests.Factory`) starts a real Kestrel server on an **ephemeral port** (the engine's `IHttpClientFactory` needs actual sockets), so it runs in parallel with the other web-host fixtures. Flow files are read from build output and their `http://localhost:5001` resources are rewritten to the fixture's base URL at registration time.

## Adding a new scenario

1. Create `flow-scenarios/<scenario-name>/flows/*.json` — one file per flow, each with a top-level `"Id"` (its stable logical id) and `"Comment"`.
2. Reference other flows in the same scenario as `Task` states with `"Resource": "flow://<Id>"`; use `Catch` for failure routing.
3. Point HTTP tasks at fake endpoints (`http://localhost:5001/api/fake/...`) — add new endpoints to `FakeCommerceController.cs` (or a sibling controller) backed by the store singleton when existing ones don't fit.
4. Add `<scenario-name>ScenarioTests.cs` in `StepFunctionsApp.Tests` following the fixture pattern above; assert on execution results **and** observable side effects (outbox, inventory levels).
