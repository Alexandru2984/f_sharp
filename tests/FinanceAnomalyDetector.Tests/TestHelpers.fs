module TestHelpers

open System
open FinanceAnomalyDetector

/// Builds an in-memory expense for pure-function tests.
let mkExpense id (amount: decimal) category merchant (date: DateTime) : Expense =
    { Id = id
      UserId = 1
      Amount = amount
      Currency = "USD"
      Category = category
      Merchant = merchant
      Description = ""
      Date = date
      CreatedAt = DateTime.UtcNow }

let mkDto (amount: decimal) category merchant (date: DateTime) : ExpenseDto =
    { Amount = amount
      Currency = "USD"
      Category = category
      Merchant = merchant
      Description = ""
      Date = date }
