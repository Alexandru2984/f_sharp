namespace FinanceAnomalyDetector

open System

[<CLIMutable>]
type Expense = {
    Id : int
    Amount : decimal
    Currency : string
    Category : string
    Merchant : string
    Description : string
    Date : DateTime
    CreatedAt : DateTime
}

[<CLIMutable>]
type Anomaly = {
    Id : int
    ExpenseId : int
    Score : int
    Severity : string
    Reason : string
    Recommendation : string
    DetectedAt : DateTime
    IsResolved : bool
}

[<CLIMutable>]
type Budget = {
    Category : string
    LimitAmount : decimal
}

type ImportResult = {
    ImportedRows : int
    SkippedRows : int
    ValidationErrors : string list
}

type ExpenseDto = {
    Amount : decimal
    Currency : string
    Category : string
    Merchant : string
    Description : string
    Date : DateTime
}
