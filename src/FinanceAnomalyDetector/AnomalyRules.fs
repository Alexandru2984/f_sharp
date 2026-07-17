namespace FinanceAnomalyDetector

open System

/// Pure detection rules. Each rule returns at most one anomaly per expense,
/// tagged with a stable RuleCode so resolved findings survive re-runs.
module AnomalyRules =

    let getSeverity score =
        if score >= 90 then "critical"
        elif score >= 70 then "high"
        elif score >= 40 then "medium"
        else "low"

    let mkAnomaly (expenseId: int) (code: string) (score: int) (reason: string) (recommendation: string) =
        { Id = 0
          UserId = 0
          ExpenseId = expenseId
          RuleCode = code
          Score = score
          Severity = getSeverity score
          Reason = reason
          Recommendation = recommendation
          DetectedAt = DateTime.UtcNow
          IsResolved = false }

    let private sampleStdDev (values: float list) =
        let mean = List.average values
        let sumSquares = values |> List.sumBy (fun v -> (v - mean) ** 2.0)
        sqrt (sumSquares / float (values.Length - 1))

    /// CAT_OUTLIER: statistical outlier within the expense's category.
    /// Uses a z-score when there is enough history (n >= 5), otherwise a
    /// simple 3x-average heuristic for small samples (3 <= n < 5).
    let checkCategoryOutlier (expense: Expense) (history: Expense list) =
        let categoryHistory = history |> List.filter (fun e -> e.Category = expense.Category && e.Id <> expense.Id)
        if categoryHistory.Length >= 5 then
            let amounts = categoryHistory |> List.map (fun e -> float e.Amount)
            let mean = List.average amounts
            let stdDev = sampleStdDev amounts
            if stdDev > 0.0 then
                let z = (float expense.Amount - mean) / stdDev
                if z >= 3.0 then
                    let score = min 100 (50 + int (z * 10.0))
                    Some (mkAnomaly expense.Id "CAT_OUTLIER" score
                            (sprintf "Amount is %.1f standard deviations above the '%s' average." z expense.Category)
                            "Review this transaction and check if it was intentional.")
                else None
            elif mean > 0.0 && float expense.Amount > mean * 3.0 then
                // Identical historical amounts: fall back to the multiple check.
                let score = 70
                Some (mkAnomaly expense.Id "CAT_OUTLIER" score
                        (sprintf "Amount breaks a perfectly stable '%s' spending pattern." expense.Category)
                        "Review this transaction and check if it was intentional.")
            else None
        elif categoryHistory.Length >= 3 then
            let avg = categoryHistory |> List.averageBy (fun e -> e.Amount)
            if avg > 0m && expense.Amount > avg * 3m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 50)
                Some (mkAnomaly expense.Id "CAT_OUTLIER" score
                        (sprintf "Expense is %.1fx higher than the category average." (float (expense.Amount / avg)))
                        "Review this transaction and check if it was intentional.")
            else None
        else None

    /// MERCHANT_SPIKE: unusually large amount versus the merchant's history.
    let checkMerchantSpike (expense: Expense) (history: Expense list) =
        let merchantHistory = history |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id)
        if merchantHistory.Length < 2 then None
        else
            let avg = merchantHistory |> List.averageBy (fun e -> e.Amount)
            if avg = 0m then None
            elif expense.Amount > avg * 4m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 40)
                Some (mkAnomaly expense.Id "MERCHANT_SPIKE" score
                        (sprintf "Unusually large amount for merchant %s." expense.Merchant)
                        "Verify the purchase amount.")
            else None

    /// CAT_CHANGE: merchant appears under a category it normally isn't in.
    let checkCategoryChange (expense: Expense) (history: Expense list) =
        let merchantHistory = history |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id)
        if merchantHistory.Length < 3 then None
        else
            let categories = merchantHistory |> List.countBy (fun e -> e.Category) |> List.sortByDescending snd
            let mainCategory = fst categories.Head
            if mainCategory <> expense.Category then
                Some (mkAnomaly expense.Id "CAT_CHANGE" 65
                        (sprintf "Merchant '%s' is usually in '%s' but this is '%s'." expense.Merchant mainCategory expense.Category)
                        "Check for misclassification.")
            else None

    /// DUPLICATE: same merchant and amount within a 10-minute window.
    /// Only the later of the pair is flagged, so one incident yields one anomaly.
    let checkDuplicate (expense: Expense) (history: Expense list) =
        let isDuplicateOf (e: Expense) =
            e.Id < expense.Id
            && e.Merchant = expense.Merchant
            && e.Amount = expense.Amount
            && abs (e.Date - expense.Date).TotalMinutes <= 10.0
        if history |> List.exists isDuplicateOf then
            Some (mkAnomaly expense.Id "DUPLICATE" 85
                    (sprintf "Possible duplicate charge of %.2M at %s within 10 minutes." expense.Amount expense.Merchant)
                    "Check whether you were charged twice for the same purchase.")
        else None

    /// SUB_HIKE: a stable recurring charge (subscription, rent, ...) suddenly
    /// costs 20%+ more than its historical average.
    let checkSubscriptionHike (expense: Expense) (history: Expense list) =
        let past =
            history
            |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id && e.Date < expense.Date)
        match Recurring.detect past |> List.tryFind (fun r -> r.Merchant = expense.Merchant) with
        | Some r when r.AverageAmount > 0m
                      && expense.Amount > r.AverageAmount * 1.2m
                      && expense.Amount - r.AverageAmount > 1m ->
            let pct = int (((expense.Amount / r.AverageAmount) - 1m) * 100m)
            Some (mkAnomaly expense.Id "SUB_HIKE" 60
                    (sprintf "Recurring charge at %s is %d%% above its usual %.2M." expense.Merchant pct r.AverageAmount)
                    "Check whether this subscription's price increased.")
        | _ -> None

    /// NIGHT: transaction timestamped deep in the night (1 AM - 4 AM).
    let checkNightSpending (expense: Expense) =
        let hour = expense.Date.Hour
        if hour >= 1 && hour <= 4 then
            Some (mkAnomaly expense.Id "NIGHT" 50
                    "Transaction occurred late at night (1 AM - 4 AM)."
                    "Confirm if you made this purchase.")
        else None
