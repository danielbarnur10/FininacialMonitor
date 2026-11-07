export type TransactionStatus = 'Pending' | 'Completed' | 'Failed'

export interface Transaction {
  transactionId: string
  amount: number
  currency: string
  status: TransactionStatus
  timestamp: string
}