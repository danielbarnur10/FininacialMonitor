import './MonitorPage.css'
import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getTransactions } from '../services/transactionApi'
import type { Transaction, TransactionStatus } from '../types/transaction'

type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'
type StatusFilter = 'All' | TransactionStatus

type TransactionRow = {
  id: number
  transaction: Transaction
  isLive: boolean
}

const MAX_TRANSACTIONS = 1000
const LIVE_BATCH_MS = 50
let nextRowId = 0

function makeRow(transaction: Transaction, isLive: boolean): TransactionRow {
  return { id: nextRowId++, transaction, isLive }
}

export function MonitorPage() {
  const [transactions, setTransactions] = useState<TransactionRow[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('connecting')
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All')
  // A WeakSet keeps an arrival from animating again if its row is filtered and re-shown.
  const animatedRows = useRef(new WeakSet<TransactionRow>())

  useEffect(() => {
    let disposed = false
    let loadingSnapshot = true
    let bufferedTransactions: TransactionRow[] = []
    let pendingLive: TransactionRow[] = []
    let flushTimer: ReturnType<typeof setTimeout> | undefined
    let snapshotVersion = 0
    let snapshotController: AbortController | undefined

    const connection = new signalR.HubConnectionBuilder()
      // A WebSocket-only connection avoids negotiated requests hitting different pods.
      .withUrl('http://127.0.0.1:5080/hubs/transactions', {
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect()
      .build()

    function flushLive() {
      flushTimer = undefined
      if (disposed || pendingLive.length === 0) return
      const batch = pendingLive
      pendingLive = []
      setTransactions((current) => [...batch, ...current].slice(0, MAX_TRANSACTIONS))
    }

    function handleTransaction(transaction: Transaction) {
      if (disposed) return
      const row = makeRow(transaction, true)

      if (loadingSnapshot) {
        bufferedTransactions.unshift(row)
        bufferedTransactions = bufferedTransactions.slice(0, MAX_TRANSACTIONS)
        return
      }

      // Group fast arrivals into a single render and bound the pending buffer.
      pendingLive.unshift(row)
      if (pendingLive.length > MAX_TRANSACTIONS) pendingLive.pop()
      if (flushTimer === undefined) flushTimer = setTimeout(flushLive, LIVE_BATCH_MS)
    }

    async function refreshSnapshot() {
      snapshotController?.abort()
      const controller = new AbortController()
      snapshotController = controller
      const currentVersion = ++snapshotVersion

      // Retain events queued just before a reconnect while the snapshot is loading.
      if (flushTimer !== undefined) {
        clearTimeout(flushTimer)
        flushTimer = undefined
      }
      bufferedTransactions = [...pendingLive, ...bufferedTransactions].slice(0, MAX_TRANSACTIONS)
      pendingLive = []
      loadingSnapshot = true
      setIsLoading(true)
      setError('')

      try {
        const snapshot = await getTransactions(controller.signal)
        if (disposed || currentVersion !== snapshotVersion) return

        const rows = snapshot.map((transaction) => makeRow(transaction, false))
        setTransactions([...bufferedTransactions, ...rows].slice(0, MAX_TRANSACTIONS))
      } catch (caughtError) {
        if (disposed || controller.signal.aborted || currentVersion !== snapshotVersion) return
        const buffered = bufferedTransactions
        setTransactions((current) => [...buffered, ...current].slice(0, MAX_TRANSACTIONS))
        setError(caughtError instanceof Error ? caughtError.message : 'Could not load transactions.')
      } finally {
        if (!disposed && currentVersion === snapshotVersion) {
          loadingSnapshot = false
          bufferedTransactions = []
          setIsLoading(false)
        }
      }
    }

    connection.on('TransactionReceived', handleTransaction)
    connection.onreconnecting(() => {
      if (!disposed) setConnectionStatus('reconnecting')
    })
    connection.onreconnected(() => {
      if (disposed) return
      setConnectionStatus('connected')
      void refreshSnapshot()
    })
    connection.onclose(() => {
      if (!disposed) setConnectionStatus('disconnected')
    })

    async function initialize() {
      try {
        await connection.start()
        if (disposed) return
        setConnectionStatus('connected')
      } catch {
        if (disposed) return
        setConnectionStatus('disconnected')
      }
      await refreshSnapshot()
    }

    void initialize()
    return () => {
      disposed = true
      snapshotController?.abort()
      if (flushTimer !== undefined) clearTimeout(flushTimer)
      connection.off('TransactionReceived', handleTransaction)
      void connection.stop()
    }
  }, [])

  const latestById = new Map<string, Transaction>()
  for (const { transaction } of transactions) {
    if (!latestById.has(transaction.transactionId)) {
      latestById.set(transaction.transactionId, transaction)
    }
  }

  const visibleTransactions = statusFilter === 'All'
    ? transactions
    : transactions.filter(({ transaction }) => transaction.status === statusFilter)
  const visibleLatest = [...latestById.values()].filter(
    (transaction) => statusFilter === 'All' || transaction.status === statusFilter,
  )

  return (
    <main>
      <h1>Live Monitor</h1>
      <p role="status">Connection: {connectionStatus}</p>
      <label htmlFor="status-filter">Filter by status: </label>
      <select
        id="status-filter"
        value={statusFilter}
        onChange={(event) => setStatusFilter(event.target.value as StatusFilter)}
      >
        <option value="All">All</option>
        <option value="Pending">Pending</option>
        <option value="Completed">Completed</option>
        <option value="Failed">Failed</option>
      </select>
      {isLoading && <p>Loading transactions...</p>}
      {error && <p role="alert">{error}</p>}
      {!isLoading && !error && transactions.length === 0 && <p>No transactions yet.</p>}
      {!isLoading && transactions.length > 0 && visibleTransactions.length === 0 && (
        <p>No transactions match this filter.</p>
      )}

      {visibleLatest.length > 0 && (
        <section aria-labelledby="current-transactions-heading">
          <h2 id="current-transactions-heading">Latest observed status</h2>
          <p>Shows the most recently observed update for each transaction.</p>
          <ul>
            {visibleLatest.map((transaction) => (
              <li key={transaction.transactionId}>
                <strong className={`status status--${transaction.status.toLowerCase()}`}>
                  {transaction.status}
                </strong>
                {' — '}{transaction.amount} {transaction.currency}
                <br />ID: {transaction.transactionId}
              </li>
            ))}
          </ul>
        </section>
      )}

      {visibleTransactions.length > 0 && (
        <section aria-labelledby="transaction-activity-heading">
          <h2 id="transaction-activity-heading">Recent activity</h2>
          <ul>
            {visibleTransactions.map((row) => (
              <li
                key={row.id}
                className={row.isLive && !animatedRows.current.has(row) ? 'transaction-arrival' : undefined}
                onAnimationEnd={() => animatedRows.current.add(row)}
              >
                <strong className={`status status--${row.transaction.status.toLowerCase()}`}>
                  {row.transaction.status}
                </strong>
                {' — '}{row.transaction.amount} {row.transaction.currency}
                {' — '}{new Date(row.transaction.timestamp).toLocaleString()}
                <br />ID: {row.transaction.transactionId}
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  )
}
