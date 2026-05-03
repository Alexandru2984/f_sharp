namespace FinanceAnomalyDetector

open System

module AnomalyRules =
    
    let getSeverity score =
        if score >= 90 then "critical"
        elif score >= 70 then "high"
        elif score >= 40 then "medium"
        else "low"

    let checkCategoryAverage (expense: Expense) (history: Expense list) =
        let categoryHistory = history |> List.filter (fun e -> e.Category = expense.Category && e.Id <> expense.Id)
        if categoryHistory.Length < 3 then None
        else
            let avg = categoryHistory |> List.averageBy (fun e -> e.Amount)
            if avg = 0m then None
            elif expense.Amount > avg * 3m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 50)
                Some { Id = 0; UserId = 0; ExpenseId = expense.Id; Score = score; Severity = getSeverity score; Reason = sprintf "Expense is %.1fx higher than the category average." (expense.Amount / avg); Recommendation = "Review this transaction and check if it was intentional."; DetectedAt = DateTime.UtcNow; IsResolved = false }
            else None

    let checkMerchantSpike (expense: Expense) (history: Expense list) =
        let merchantHistory = history |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id)
        if merchantHistory.Length < 2 then None
        else
            let avg = merchantHistory |> List.averageBy (fun e -> e.Amount)
            if avg = 0m then None
            elif expense.Amount > avg * 4m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 40)
                Some { Id = 0; UserId = 0; ExpenseId = expense.Id; Score = score; Severity = getSeverity score; Reason = sprintf "Unusually large amount for merchant %s." expense.Merchant; Recommendation = "Verify the purchase amount."; DetectedAt = DateTime.UtcNow; IsResolved = false }
            else None

    let checkCategoryChange (expense: Expense) (history: Expense list) =
        let merchantHistory = history |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id)
        if merchantHistory.Length < 3 then None
        else
            let categories = merchantHistory |> List.groupBy (fun e -> e.Category) |> List.map (fun (k, v) -> k, v.Length) |> List.sortByDescending snd
            let mainCategory = fst categories.Head
            if mainCategory <> expense.Category then
                let score = 65
                Some { Id = 0; UserId = 0; ExpenseId = expense.Id; Score = score; Severity = getSeverity score; Reason = sprintf "Merchant '%s' is usually in '%s' but this is '%s'." expense.Merchant mainCategory expense.Category; Recommendation = "Check for misclassification."; DetectedAt = DateTime.UtcNow; IsResolved = false }
            else None
            
    let checkNightSpending (expense: Expense) =
        let hour = expense.Date.Hour
        if hour >= 1 && hour <= 4 then
            let score = 50
            Some { Id = 0; UserId = 0; ExpenseId = expense.Id; Score = score; Severity = getSeverity score; Reason = "Transaction occurred late at night (1 AM - 4 AM)."; Recommendation = "Confirm if you made this purchase."; DetectedAt = DateTime.UtcNow; IsResolved = false }
        else None
