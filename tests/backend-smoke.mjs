// Run against a running backend with Node.js 20.17 or later.
import { spawnSync } from "node:child_process";

// Node 20 supports the built-in WebSocket client behind this flag.
if (typeof WebSocket === "undefined") {
  const result = spawnSync(process.execPath, ["--experimental-websocket", ...process.argv.slice(1)], {
    stdio: "inherit",
  });
  process.exit(result.status ?? 1);
}

const base = process.env.API_URL ?? "http://127.0.0.1:5080";
const separator = "\x1e";

function check(condition, message) {
  if (!condition) throw new Error(message);
}

async function connectClient() {
  const response = await fetch(`${base}/hubs/transactions/negotiate?negotiateVersion=1`, {
    method: "POST",
  });
  check(response.ok, `SignalR negotiation failed: ${response.status}`);
  const { connectionToken } = await response.json();
  const socketUrl = new URL("/hubs/transactions", base);
  socketUrl.protocol = socketUrl.protocol === "https:" ? "wss:" : "ws:";
  socketUrl.searchParams.set("id", connectionToken);
  const socket = new WebSocket(socketUrl);
  const events = [];

  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("SignalR handshake timed out")), 5000);
    let handshaken = false;
    socket.addEventListener("open", () => {
      socket.send(JSON.stringify({ protocol: "json", version: 1 }) + separator);
    });
    socket.addEventListener("error", () => {
      clearTimeout(timer);
      reject(new Error("WebSocket connection failed"));
    });
    socket.addEventListener("message", ({ data }) => {
      for (const part of String(data).split(separator).filter(Boolean)) {
        const message = JSON.parse(part);
        if (!handshaken) {
          clearTimeout(timer);
          if (message.error) {
            reject(new Error(`SignalR handshake failed: ${message.error}`));
            return;
          }
          handshaken = true;
          resolve();
        } else if (message.type === 1 && message.target === "TransactionReceived") {
          events.push(message.arguments[0]);
        }
      }
    });
  });

  return { socket, events };
}

async function waitFor(predicate) {
  const deadline = Date.now() + 5000;
  while (!predicate()) {
    if (Date.now() >= deadline) throw new Error("Timed out waiting for live updates");
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}

const clients = [];
try {
  clients.push(await connectClient());
  clients.push(await connectClient());

  const transaction = {
    transactionId: crypto.randomUUID(),
    amount: 1500.5,
    currency: "USD",
    status: "Pending",
    timestamp: new Date().toISOString(),
  };
  const post = (body) => fetch(`${base}/api/transactions`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });

  const malformed = await fetch(`${base}/api/transactions`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: '{"amount":',
  });
  check(malformed.status === 400, `Expected malformed JSON to return 400, got ${malformed.status}`);

  for (const field of ["transactionId", "amount", "currency", "status", "timestamp"]) {
    const body = { ...transaction };
    delete body[field];
    const response = await post(body);
    check(response.status === 400, `Expected missing ${field} to return 400, got ${response.status}`);
  }

  const nullStatus = await post({ ...transaction, status: null });
  check(nullStatus.status === 400, `Expected null status to return 400, got ${nullStatus.status}`);
  const invalidAmountType = await post({ ...transaction, amount: "not-a-number" });
  check(invalidAmountType.status === 400, `Expected invalid amount type to return 400, got ${invalidAmountType.status}`);
  const invalid = await post({ ...transaction, amount: 0 });
  check(invalid.status === 400, `Expected invalid amount to return 400, got ${invalid.status}`);
  const invalidStatus = await post({ ...transaction, status: "Unknown" });
  check(invalidStatus.status === 400, `Expected invalid status to return 400, got ${invalidStatus.status}`);
  const numericStatus = await post({ ...transaction, status: 0 });
  check(numericStatus.status === 400, `Expected numeric status to return 400, got ${numericStatus.status}`);

  const beforeValidPost = await fetch(`${base}/api/transactions`);
  check(beforeValidPost.ok, `Snapshot before valid POST failed: ${beforeValidPost.status}`);
  const beforeSnapshot = await beforeValidPost.json();
  check(!beforeSnapshot.some((item) => item.transactionId === transaction.transactionId), "Invalid request was stored");

  const accepted = await post(transaction);
  if (accepted.status !== 201) {
    throw new Error(`Expected 201, got ${accepted.status}: ${await accepted.text()}`);
  }
  const created = await accepted.json();
  check(created.transactionId === transaction.transactionId && created.status === "Pending", "Unexpected POST response");

  await waitFor(() => clients.every(({ events }) => events.some((item) => item.transactionId === transaction.transactionId)));
  for (const { events } of clients) {
    const received = events.find((item) => item.transactionId === transaction.transactionId);
    check(received.status === "Pending" && received.amount === transaction.amount, "Unexpected SignalR payload");
  }

  const burst = Array.from({ length: 20 }, () => ({ ...transaction, transactionId: crypto.randomUUID() }));
  const burstResponses = await Promise.all(burst.map(post));
  check(burstResponses.every((response) => response.status === 201), "A concurrent POST failed");
  const burstIds = new Set(burst.map((item) => item.transactionId));
  await waitFor(() => clients.every(({ events }) =>
    events.filter((item) => burstIds.has(item.transactionId)).length === burst.length));

  const snapshotResponse = await fetch(`${base}/api/transactions`);
  check(snapshotResponse.ok, `Snapshot failed: ${snapshotResponse.status}`);
  check(snapshotResponse.headers.get("cache-control")?.toLowerCase().includes("no-store"),
    "Snapshot response must set Cache-Control: no-store");
  const snapshot = await snapshotResponse.json();
  check(snapshot.some((item) => item.transactionId === transaction.transactionId), "Saved transaction missing from snapshot");
  console.log("Backend smoke test passed: validation, concurrent POSTs, snapshot, and two SignalR clients.");
} finally {
  for (const { socket } of clients) socket.close();
}
