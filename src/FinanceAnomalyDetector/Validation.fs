namespace FinanceAnomalyDetector

open System
open System.Text.RegularExpressions

module Validation =
    let private usernameRegex = Regex(@"^[a-zA-Z0-9._-]{3,50}$", RegexOptions.Compiled)

    let validateRegistration (username: string) (password: string) =
        let errors = [
            if isNull username || not (usernameRegex.IsMatch(username)) then
                yield "Username must be 3-50 characters using only letters, digits, dot, underscore or dash."
            if isNull password || password.Length < 8 then
                yield "Password must be at least 8 characters."
            if not (isNull password) && password.Length > 128 then
                yield "Password must be at most 128 characters."
        ]
        if errors.IsEmpty then Ok () else Error errors

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
