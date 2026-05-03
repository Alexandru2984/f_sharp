namespace FinanceAnomalyDetector

open System

module Validation =
    let isValidAmount (amount: decimal) =
        amount > 0m && amount < 100000000m

    let isValidString (maxLength: int) (str: string) =
        not (String.IsNullOrWhiteSpace(str)) && str.Length <= maxLength

    let validateExpense (dto: ExpenseDto) =
        let errors = [
            if not (isValidAmount dto.Amount) then yield "Amount must be greater than 0 and less than 100,000,000."
            if not (isValidString 10 dto.Currency) then yield "Currency is required and max 10 chars."
            if not (isValidString 100 dto.Category) then yield "Category is required and max 100 chars."
            if not (isValidString 200 dto.Merchant) then yield "Merchant is required and max 200 chars."
        ]
        
        if errors.IsEmpty then Ok dto else Error errors
