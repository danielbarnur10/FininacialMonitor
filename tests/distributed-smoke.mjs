// Forward two different backend pods to 5081 and 5082 before running.
// Override with POD_A_URL and POD_B_URL when using other addresses.
import { spawnSync } from 'node:child_process'

// Node 20.17 exposes WebSocket behind a flag; newer releases expose it by default.
if (typeof WebSocket === 'undefined') {
  if (process.execArgv.includes('--experimental-websocket')) {
    throw new Error('This Node version does not provide WebSocket; use Node 22 or later.')
  }
  const result = spawnSync(process.execPath, ['--experimental-websocket', ...process.argv.slice(1)], {
    stdio: 'inherit',
    env: process.env,
  })
  process.exit(result.status ?? 1)
}

const podA = (process.env.POD_A_URL ?? 'http://127.0.0.1:5081').replace(/\/$/, '')
const podB = (process.env.POD_B_URL ?? 'http://127.0.0.1:5082').replace(/\/$/, '')
const separator = '\x1e'

function check(condition, message) {
  if (!condition) throw new Error(message)
}

check(new URL(podA).origin !== new URL(podB).origin, 'Use different addresses for the two pod forwards.')

async function connect(base) {
  const url = new URL('/hubs/transactions', base)
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  // Direct WebSocket: SignalR skipNegotiation avoids sticky-session requirements.
  const socket = new WebSocket(url)
  const events = []

  try {
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('SignalR handshake timed out')), 10_000)
      let connected = false
      socket.addEventListener('open', () => {
        socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + separator)
      })
      socket.addEventListener('error', () => {
        clearTimeout(timer)
        reject(new Error(`WebSocket connection to ${base} failed`))
      })
      socket.addEventListener('close', () => {
        if (!connected) {
          clearTimeout(timer)
          reject(new Error(`WebSocket connection to ${base} closed during handshake`))
        }
      })
      socket.addEventListener('message', ({ data }) => {
        try {
          for (const part of String(data).split(separator).filter(Boolean)) {
            const message = JSON.parse(part)
            if (!connected) {
              clearTimeout(timer)
              if (message.error) {
                reject(new Error(`SignalR handshake failed: ${message.error}`))
                return
              }
              connected = true
              resolve()
            } else if (message.type === 1 && message.target === 'TransactionReceived') {
              events.push(message.arguments[0])
            }
          }
        } catch (error) {
          clearTimeout(timer)
          reject(error)
        }
      })
    })
  } catch (error) {
    socket.close()
    throw error
  }

  return { socket, events }
}

async function waitFor(predicate, description) {
  const deadline = Date.now() + 10_000
  while (!predicate()) {
    if (Date.now() >= deadline) throw new Error(`Timed out waiting for ${description}`)
    await new Promise((resolve) => setTimeout(resolve, 25))
  }
}

async function readSnapshot(base) {
  const response = await fetch(`${base}/api/transactions`, { signal: AbortSignal.timeout(10_000) })
  check(response.ok, `Snapshot from ${base} failed: HTTP ${response.status}`)
  const transactions = await response.json()
  check(Array.isArray(transactions), `Snapshot from ${base} was not an array`)
  return transactions
}

async function postTransaction(base, transaction) {
  const response = await fetch(`${base}/api/transactions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(transaction),
    signal: AbortSignal.timeout(10_000),
  })
  if (response.status !== 201) {
    throw new Error(`POST to ${base}: expected 201, received ${response.status}: ${await response.text()}`)
  }
}

let client
let secondClient
try {
  client = await connect(podA)
  secondClient = await connect(podB)

  const transaction = {
    transactionId: crypto.randomUUID(),
    amount: 1500.5,
    currency: 'USD',
    status: 'Pending',
    timestamp: new Date().toISOString(),
  }

  // Confirm pod A has completed its own SignalR subscription before testing cross-pod delivery.
  const probe = { ...transaction, transactionId: crypto.randomUUID() }
  await postTransaction(podA, probe)
  await waitFor(
    () => [client, secondClient].every(({ events }) =>
      events.some((item) => item.transactionId === probe.transactionId)),
    'the subscription probe on both pods',
  )

  await postTransaction(podB, transaction)

  await waitFor(
    () => client.events.some((item) => item.transactionId === transaction.transactionId),
    'the transaction on the client connected to pod A',
  )
  const event = client.events.find((item) => item.transactionId === transaction.transactionId)
  check(event.status === transaction.status && event.amount === transaction.amount, 'Unexpected broadcast payload')

  const snapshots = await Promise.all([readSnapshot(podA), readSnapshot(podB)])
  for (const [index, snapshot] of snapshots.entries()) {
    check(
      snapshot.some((item) => item.transactionId === transaction.transactionId && item.status === transaction.status),
      `Transaction missing from pod ${index === 0 ? 'A' : 'B'} snapshot`,
    )
  }

  console.log('Distributed smoke test passed: POST to B reached a client on A and both snapshots contain it.')

  const completed = { ...transaction, status: 'Completed', timestamp: new Date().toISOString() }
  await postTransaction(podB, completed)
  await waitFor(
    () => [client, secondClient].every(({ events }) => events.some((item) =>
      item.transactionId === transaction.transactionId && item.status === 'Completed')),
    'the status update on both connected clients',
  )
  const updatedSnapshots = await Promise.all([readSnapshot(podA), readSnapshot(podB)])
  for (const [index, snapshot] of updatedSnapshots.entries()) {
    const statuses = new Set(snapshot.filter((item) => item.transactionId === transaction.transactionId).map((item) => item.status))
    check(statuses.has('Pending') && statuses.has('Completed'), `Pod ${index === 0 ? 'A' : 'B'} snapshot lost a status update`)
  }
  console.log('Both pods retained and broadcast the same-ID status change.')

  // Distinct IDs make it possible to check delivery without assuming broadcast order.
  const burst = Array.from({ length: 100 }, (_, index) => ({
    ...transaction,
    transactionId: crypto.randomUUID(),
    amount: index + 1,
    status: index % 2 === 0 ? 'Completed' : 'Failed',
  }))
  const burstIds = new Set(burst.map((item) => item.transactionId))
  await Promise.all(burst.map((item, index) => postTransaction(index % 2 === 0 ? podA : podB, item)))
  await waitFor(
    () => [client, secondClient].every(({ events }) =>
      new Set(events.filter((item) => burstIds.has(item.transactionId)).map((item) => item.transactionId)).size === burst.length),
    '100 concurrent updates on both connected clients',
  )
  const burstSnapshots = await Promise.all([readSnapshot(podA), readSnapshot(podB)])
  for (const [index, snapshot] of burstSnapshots.entries()) {
    const found = new Set(snapshot.filter((item) => burstIds.has(item.transactionId)).map((item) => item.transactionId))
    check(found.size === burst.length, `Pod ${index === 0 ? 'A' : 'B'} snapshot is missing burst updates`)
  }

  console.log('100 concurrent updates reached both clients and both shared snapshots.')
  console.log('Ensure the two URLs were forwarded to different pods; the script cannot identify the pods itself.')
} finally {
  client?.socket.close()
  secondClient?.socket.close()
}
