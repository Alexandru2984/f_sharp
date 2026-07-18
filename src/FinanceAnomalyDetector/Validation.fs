namespace FinanceAnomalyDetector

open System
open System.Text.RegularExpressions

module Validation =
    let private usernameRegex = Regex(@"^[a-zA-Z0-9._-]{3,50}$", RegexOptions.Compiled)

    [<Literal>]
    let MinPasswordLength = 12

    let validatePassword (password: string) =
        let errors = [
            if isNull password || password.Length < MinPasswordLength then
                yield sprintf "Password must be at least %d characters." MinPasswordLength
            if not (isNull password) && password.Length > 128 then
                yield "Password must be at most 128 characters."
        ]
        if errors.IsEmpty then Ok () else Error errors

    let validateRegistration (username: string) (password: string) =
        let usernameErrors = [
            if isNull username || not (usernameRegex.IsMatch(username)) then
                yield "Username must be 3-50 characters using only letters, digits, dot, underscore or dash."
        ]
        let passwordErrors = match validatePassword password with Ok () -> [] | Error errs -> errs
        match usernameErrors @ passwordErrors with
        | [] -> Ok ()
        | errors -> Error errors

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
            if not (isNull dto.Description) && dto.Description.Length > 500 then yield "Description max 500 chars."
        ]

        if errors.IsEmpty then Ok dto else Error errors
