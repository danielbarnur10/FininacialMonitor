import "./MonitorPage.css";
import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { getTransactions } from "../services/transactionApi";
import type { Transaction, TransactionStatus } from "../types/transaction";
type ConnectionStatus =
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected";
type StatusFilter = "All" | TransactionStatus;
const MAX_TRANSACTIONS = 1000;

export function MonitorPage() {
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState("");
  const [connectionStatus, setConnectionStatus] =
    useState<ConnectionStatus>("connecting");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");

  useEffect(() => {
    let disposed = false;
    let loadingSnapshot = true;
    let bufferedTransactions: Transaction[] = [];

    let snapshotVersion = 0;
    let snapshotController: AbortController | undefined;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("http://127.0.0.1:5080/hubs/transactions")
      .withAutomaticReconnect()
      .build();

    function handleTransaction(transaction: Transaction) {
      if (disposed) return;

      if (loadingSnapshot) {
        bufferedTransactions.unshift(transaction);
        bufferedTransactions = bufferedTransactions.slice(0, MAX_TRANSACTIONS);
        return;
      }

      setTransactions((current) =>
        [transaction, ...current].slice(0, MAX_TRANSACTIONS),
      );
    }

    async function refreshSnapshot() {
      // Cancel an older snapshot request if one is still running.
      snapshotController?.abort();

      const controller = new AbortController();
      snapshotController = controller;

      const currentVersion = ++snapshotVersion;

      loadingSnapshot = true;
      setIsLoading(true);
      setError("");

      try {
        const snapshot = await getTransactions(controller.signal);

        if (disposed || currentVersion !== snapshotVersion) return;

        setTransactions(
          [...bufferedTransactions, ...snapshot].slice(0, MAX_TRANSACTIONS),
        );
      } catch (caughtError) {
        if (
          disposed ||
          controller.signal.aborted ||
          currentVersion !== snapshotVersion
        ) {
          return;
        }

        // Keep the current list and any live events received during the request.
        const buffered = bufferedTransactions;

        setTransactions((current) =>
          [...buffered, ...current].slice(0, MAX_TRANSACTIONS),
        );

        setError(
          caughtError instanceof Error
            ? caughtError.message
            : "Could not load transactions.",
        );
      } finally {
        if (!disposed && currentVersion === snapshotVersion) {
          loadingSnapshot = false;
          bufferedTransactions = [];
          setIsLoading(false);
        }
      }
    }

    connection.on("TransactionReceived", handleTransaction);

    connection.onreconnecting(() => {
      if (!disposed) setConnectionStatus("reconnecting");
    });

    connection.onreconnected(() => {
      if (disposed) return;

      setConnectionStatus("connected");
      void refreshSnapshot();
    });

    connection.onclose(() => {
      if (!disposed) setConnectionStatus("disconnected");
    });

    async function initialize() {
      try {
        await connection.start();

        if (disposed) return;

        setConnectionStatus("connected");
      } catch {
        if (disposed) return;

        setConnectionStatus("disconnected");
      }

      await refreshSnapshot();
    }

    void initialize();

    return () => {
      disposed = true;
      snapshotController?.abort();
      connection.off("TransactionReceived", handleTransaction);
      void connection.stop();
    };
  }, []);
  const visibleTransactions =
    statusFilter === "All"
      ? transactions
      : transactions.filter(
          (transaction) => transaction.status === statusFilter,
        );
  return (
    <main>
      <h1>Live Monitor</h1>
      <p>Connection: {connectionStatus}</p>
      <label htmlFor="status-filter">Filter by status: </label>
      <select
        id="status-filter"
        value={statusFilter}
        onChange={(event) =>
          setStatusFilter(event.target.value as StatusFilter)
        }
      >
        <option value="All">All</option>
        <option value="Pending">Pending</option>
        <option value="Completed">Completed</option>
        <option value="Failed">Failed</option>
      </select>
      {isLoading && <p>Loading transactions...</p>}
      {error && <p role="alert">{error}</p>}
      {!isLoading && !error && transactions.length === 0 && (
        <p>No transactions yet.</p>
      )}
      {!isLoading &&
        transactions.length > 0 &&
        visibleTransactions.length === 0 && (
          <p>No transactions match this filter.</p>
        )}
      {visibleTransactions.length > 0 && (
        <ul>
          {visibleTransactions.map((transaction, index) => (
            <li key={`${transaction.transactionId}-${index}`}>
              <strong
                className={`status status--${transaction.status.toLowerCase()}`}
              >
                {transaction.status}
              </strong>
              {" — "}
              {transaction.amount} {transaction.currency}
              {" — "}
              {new Date(transaction.timestamp).toLocaleString()}
              <br />
              ID: {transaction.transactionId}
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
