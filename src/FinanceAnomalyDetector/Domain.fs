namespace FinanceAnomalyDetector

open System

[<CLIMutable>]
type Expense = {
    Id : int
    UserId : int
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
    UserId : int
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
    UserId : int
    Category : string
    LimitAmount : decimal
}

[<CLIMutable>]
type User = {
    Id : int
    Username : string
    PasswordHash : string
    CreatedAt : DateTime
}

type LoginRequest = {
    Username : string
    Password : string
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