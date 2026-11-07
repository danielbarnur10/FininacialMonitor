import type { Transaction } from '../types/transaction'

const API_BASE_URL = 'http://127.0.0.1:5080'

export type SubmitTransactionResult =
  | {
      kind: 'broadcast-succeeded'
      transaction: Transaction
    }
  | {
      kind: 'broadcast-failed'
      transactionId: string
      message: string
    }

export async function submitTransaction(
  transaction: Transaction,
): Promise<SubmitTransactionResult> {
  const response = await fetch(`${API_BASE_URL}/api/transactions`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(transaction),
  })

  if (response.status === 201) {
    const savedTransaction = (await response.json()) as Transaction

    return {
      kind: 'broadcast-succeeded',
      transaction: savedTransaction,
    }
  }

  if (response.status === 202) {
    const result = (await response.json()) as {
      transactionId: string
      message: string
    }

    return {
      kind: 'broadcast-failed',
      transactionId: result.transactionId,
      message: result.message,
    }
  }
  

  throw new Error(`Could not submit transaction. HTTP ${response.status}.`)
}
export async function getTransactions(
  signal?: AbortSignal,
): Promise<Transaction[]> {
  const response = await fetch(`${API_BASE_URL}/api/transactions`, {
    signal,
  })

  if (!response.ok) {
    throw new Error(`Could not load transactions. HTTP ${response.status}.`)
  }

  return (await response.json()) as Transaction[]
}