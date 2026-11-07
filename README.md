# Financial Monitor

A real-time financial monitoring MVP built with .NET 9, React, and TypeScript.

The simulator sends transaction updates to the backend. The monitor loads
recent transactions and receives new updates through SignalR.

## Architecture

The projects follow an onion-style dependency direction:

```text
API ──────────────────> Application ──> Domain
 │                           ^
 └──> Infrastructure ────────┘
```

- **Domain** defines the immutable transaction and its rules; it has no web or storage dependency.
- **Application** coordinates storage and broadcasting through interfaces it owns.
- **Infrastructure** implements a thread-safe, in-memory transaction store.
- **API** handles HTTP input, dependency injection, the SignalR hub, and the broadcast adapter.

`TransactionService` saves first and then broadcasts. The store protects reads and writes with a lock and returns a separate, newest-first snapshot. It retains the latest 1,000 accepted updates. Multiple updates with the same transaction ID are allowed, for example a pending transaction followed by a completed one.

`Program.cs` is the composition entry point: it calls `AddFinancialMonitor`, configures middleware, maps controllers, and maps the SignalR hub. Service registrations are in `FinancialMonitor.Api/IoC/DependencyInjection.cs`; HTTP actions are in `FinancialMonitor.Api/Controllers`. The in-memory store is a singleton so every request uses the same history, the SignalR broadcaster is a singleton, and `TransactionService` is scoped to each request. No transient service is needed by the current design. Registrations live in the API because it is the composition root and can reference the outer implementations without introducing DI dependencies into the inner projects.

## Run the backend

Prerequisite: .NET 9 SDK. From this directory:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project backend/FinancialMonitor.Api \
  --no-launch-profile -- --urls http://127.0.0.1:5080
```

Development CORS allows `http://localhost:5173` and `http://127.0.0.1:5173` for the frontend. For other environments, configure `Cors:AllowedOrigins` explicitly. `GET /health` checks that the application is running.


## Run the frontend

Prerequisite: Node.js 22.12 or later.

Keep the backend running, then open a second terminal from the project root:

```bash
cd frontend
npm ci
npm run dev -- --port 5173 --strictPort
```

Open http://localhost:5173.

- `/add` contains the transaction simulator.
- `/monitor` displays recent and live transactions and supports status filtering.

The frontend is configured to call the backend at `http://127.0.0.1:5080`.

### Check the complete flow

1. Open `/monitor` and wait for the connection status to become `connected`.
2. Open `/add` in another tab.
3. Submit a transaction.
4. Confirm it appears on `/monitor`.
5. Try the status filter.

To check the frontend build:

```bash
npm run build
```

## API

`POST /api/transactions` accepts:

```json
{
  "transactionId": "4e5a13c4-995a-4ca2-9e76-5f331d8b3428",
  "amount": 1500.50,
  "currency": "USD",
  "status": "Pending",
  "timestamp": "2026-11-06T12:00:00Z"
}
```

Status must be exactly `Pending`, `Completed`, or `Failed`. For this MVP, an ID cannot be empty, the amount must be positive, currency must be three uppercase ASCII letters, and the timestamp cannot be its default value. The currency rule checks the format, not whether the code exists. Amounts use `decimal`, and timestamp offsets are retained.

- `201 Created`: stored and the SignalR send completed; the response contains the transaction.
- `400 Bad Request`: malformed or invalid input.
- `202 Accepted`: stored, but the SignalR send failed; the response has `stored: true` and `broadcastSucceeded: false`.

A completed SignalR send is not an acknowledgment from every client. A cancellation or connection failure can also leave the caller uncertain whether the save completed; IDs are not currently used for deduplication.

`GET /api/transactions` returns a JSON array of recent updates, newest accepted first. Connect a SignalR client to `/hubs/transactions` and subscribe to `TransactionReceived`; its payload has the same transaction fields as the HTTP response. A client should obtain a snapshot when connecting and after reconnecting. There is no sequence-based reconciliation yet, so snapshot/live overlap can result in duplicates, and gap-free recovery is not guaranteed.

## Tests

```bash
dotnet test tests/FinancialMonitor.Tests
```

The tests cover domain rules, service orchestration, failure handling, cancellation, storage retention, snapshots, and concurrent readers and writers.

For a live integration check, start the backend and, in another terminal, run the smoke test with Node.js 20.17+:

```bash
node tests/backend-smoke.mjs
```

It sends test transactions to the running backend and checks validation, concurrent HTTP requests, snapshots, and delivery to two SignalR clients. Set `API_URL` to use a different base URL.

## Current limits and scale-out

This is a single-process MVP. Restarting the process clears history; data is not durable. Multiple replicas would have separate stores and SignalR connection sets. To scale out, use shared durable storage and a SignalR backplane or managed SignalR service, then define ordering, idempotency, and reconnect/replay behavior. A transactional outbox is one possible way to coordinate durable writes with publishing; those components are not implemented here. Concurrent broadcasts are not guaranteed to arrive in the same order as the storage snapshot.
