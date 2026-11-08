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

`TransactionService` saves first and then broadcasts. With no Redis connection configured, the in-memory store uses a lock and returns a separate, newest-first snapshot. When Redis is configured, the Redis store shares history across backend replicas and the SignalR Redis backplane shares broadcasts. Both stores retain the latest 1,000 accepted updates. Multiple updates with the same transaction ID are allowed, for example a pending transaction followed by a completed one.

`Program.cs` is the composition entry point: it calls `AddFinancialMonitor`, configures middleware, maps controllers, and maps the SignalR hub. Service registrations are in `FinancialMonitor.Api/IoC/DependencyInjection.cs`; HTTP actions are in `FinancialMonitor.Api/Controllers`. The selected store and SignalR broadcaster are singletons, and `TransactionService` is scoped to each request. The in-memory singleton is shared only within its process; a configured Redis store is shared across processes.

## Run the backend

Prerequisite: .NET 9 SDK. From this directory:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project backend/FinancialMonitor.Api \
  --no-launch-profile -- --urls http://127.0.0.1:5080
```

Development CORS allows `http://localhost:5173` and `http://127.0.0.1:5173` for the frontend. For other environments, configure `Cors:AllowedOrigins` explicitly. `GET /health` checks that the application is running.

To run the backend on the host with Redis forwarded from Kubernetes, first run `kubectl port-forward service/financial-monitor-redis 16379:6379` in another terminal, then add `ConnectionStrings__Redis=127.0.0.1:16379` before the `dotnet run` command. When Redis is configured but unreachable, the application does not silently fall back to an independent local store.


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

The monitor shows both recent activity and the latest observed status for each transaction ID. Live arrivals are batched and animated; status colors transition when the observed status changes. Reduced-motion preferences disable these effects.

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

The separate Redis integration tests need Redis available at `127.0.0.1:16379` (for example, via the port-forward above):

```bash
dotnet test tests/FinancialMonitor.RedisIntegrationTests
```

For a live integration check, start the backend and, in another terminal, run the smoke test with Node.js 20.17+:

```bash
node tests/backend-smoke.mjs
```

It sends test transactions to the running backend and checks validation, concurrent HTTP requests, snapshots, and delivery to two SignalR clients. Set `API_URL` to use a different base URL.

## Docker and Kubernetes

Build the backend image from the repository root:

```bash
docker build -t financial-monitor-api:distributed-v1 .
```

The image contains the backend only. To run it standalone using local in-memory storage and allow the development frontend:

```bash
docker run --rm -p 127.0.0.1:5080:8080 \
  -e Cors__AllowedOrigins__0=http://localhost:5173 \
  -e Cors__AllowedOrigins__1=http://127.0.0.1:5173 \
  financial-monitor-api:distributed-v1
```

For a local Kubernetes cluster, make the image available to the cluster nodes, confirm the selected context, then apply `deploy/k8s/`. The deployment configures five API replicas and a shared Redis service. Replacing an existing pod that uses in-memory storage loses its history; export any data you need first. For example:

```bash
kubectl config current-context
kubectl apply -f deploy/k8s/
kubectl rollout status deployment/financial-monitor-redis
kubectl rollout status deployment/financial-monitor
kubectl port-forward service/financial-monitor 5080:80
```

The frontend still runs separately on port 5173. The monitor uses WebSockets with SignalR negotiation skipped so a connection doesn't require sticky sessions. `IfNotPresent` can reuse a cached image: use a new tag in the Deployment when rebuilding, and load it into each node if your cluster doesn't share Docker's image store.

To check cross-pod behavior, get the names of two different API pods with `kubectl get pods -l app=financial-monitor`, then port-forward one pod to local port 5081 and the other to 5082 (`kubectl port-forward pod/POD_NAME LOCAL_PORT:8080`). In a third terminal run:

```bash
node tests/distributed-smoke.mjs
```

The test connects SignalR clients to two pods, checks cross-pod delivery and shared snapshots, checks a same-ID status update, and sends 100 transactions concurrently. Ensure the two forwards point to different pods. `POD_A_URL` and `POD_B_URL` override the default ports.

## Current limits and scale-out

Without Redis, restarting the process clears history and replicas have separate stores. The local Kubernetes example uses a single Redis pod without authentication or a durable volume; it is a demonstration, not a fault-tolerant production database. The `/health` probes check the process, not Redis availability. Redis Pub/Sub does not replay messages lost during an outage, and saving plus broadcasting are not atomic. Concurrent broadcasts are not guaranteed to arrive in snapshot order. Because transaction updates have no unique sequence, snapshot/live overlap can produce duplicates; the latest-status view reports the latest update observed by that browser. For stronger guarantees use durable shared storage, event identifiers and replay, and consider a transactional outbox and a resilient, secured backplane.
