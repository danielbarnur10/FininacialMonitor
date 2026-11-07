import { useState, type FormEvent } from 'react'
import { submitTransaction } from '../services/transactionApi'
import type {
  Transaction,
  TransactionStatus,
} from '../types/transaction'

export function AddTransactionPage() {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('USD')
  const [status, setStatus] = useState<TransactionStatus>('Pending')

  const [isSubmitting, setIsSubmitting] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    setMessage('')
    setError('')

    const numericAmount = Number(amount)

    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError('Amount must be greater than zero.')
      return
    }

    setIsSubmitting(true)

    try {
      const transaction: Transaction = {
        transactionId: crypto.randomUUID(),
        amount: numericAmount,
        currency,
        status,
        timestamp: new Date().toISOString(),
      }

      const result = await submitTransaction(transaction)

      if (result.kind === 'broadcast-failed') {
        setMessage(result.message)
      } else {
        setMessage(
          `Transaction ${result.transaction.transactionId} was sent successfully.`,
        )
      }
    } catch (caughtError) {
      setError(
        caughtError instanceof Error
          ? caughtError.message
          : 'An unexpected error occurred.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <main>
      <h1>Transaction Simulator</h1>

      <form onSubmit={handleSubmit}>
        <div>
          <label htmlFor="amount">Amount</label>
          <input
            id="amount"
            type="number"
            min="0"
            step="any"
            required
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
          />
        </div>

        <div>
          <label htmlFor="currency">Currency</label>
          <input
            id="currency"
            type="text"
            required
            maxLength={3}
            pattern="[A-Z]{3}"
            title="Enter three uppercase letters, for example USD"
            value={currency}
            onChange={(event) => setCurrency(event.target.value.toUpperCase())}
          />
        </div>

        <div>
          <label htmlFor="status">Status</label>
          <select
            id="status"
            value={status}
            onChange={(event) =>
              setStatus(event.target.value as TransactionStatus)
            }
          >
            <option value="Pending">Pending</option>
            <option value="Completed">Completed</option>
            <option value="Failed">Failed</option>
          </select>
        </div>

        <button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Sending...' : 'Send transaction'}
        </button>
      </form>

      {message && <p role="status">{message}</p>}
      {error && <p role="alert">{error}</p>}
    </main>
  )
}