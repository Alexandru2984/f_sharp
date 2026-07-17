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

    /// Running sums over a set of expense amounts. The engine builds one per
    /// category/merchant group and derives leave-one-out views in O(1),
    /// instead of re-scanning the group for every expense.
    type AmountAggregate = {
        N : int
        Sum : float
        SumSquares : float
        SumDec : decimal
    }

    module AmountAggregate =
        let empty = { N = 0; Sum = 0.0; SumSquares = 0.0; SumDec = 0m }

        let add (amount: decimal) (agg: AmountAggregate) =
            let v = float amount
            { N = agg.N + 1; Sum = agg.Sum + v; SumSquares = agg.SumSquares + v * v; SumDec = agg.SumDec + amount }

        let ofExpenses (expenses: Expense list) =
            expenses |> List.fold (fun agg e -> add e.Amount agg) empty

        /// The aggregate without one member's amount.
        let excluding (amount: decimal) (agg: AmountAggregate) =
            let v = float amount
            { N = agg.N - 1; Sum = agg.Sum - v; SumSquares = agg.SumSquares - v * v; SumDec = agg.SumDec - amount }

    /// CAT_OUTLIER core: `agg` covers the category history excluding the
    /// expense itself. z-score when n >= 5, 3x-average for 3 <= n < 5.
    let checkCategoryOutlierAgg (expense: Expense) (agg: AmountAggregate) =
        let n = agg.N
        if n >= 5 then
            let mean = agg.Sum / float n
            let variance = max 0.0 ((agg.SumSquares - agg.Sum * agg.Sum / float n) / float (n - 1))
            let stdDev = sqrt variance
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
        elif n >= 3 then
            let avg = agg.SumDec / decimal n
            if avg > 0m && expense.Amount > avg * 3m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 50)
                Some (mkAnomaly expense.Id "CAT_OUTLIER" score
                        (sprintf "Expense is %.1fx higher than the category average." (float (expense.Amount / avg)))
                        "Review this transaction and check if it was intentional.")
            else None
        else None

    /// CAT_OUTLIER: statistical outlier within the expense's category.
    let checkCategoryOutlier (expense: Expense) (history: Expense list) =
        let agg =
            history
            |> List.filter (fun e -> e.Category = expense.Category && e.Id <> expense.Id)
            |> AmountAggregate.ofExpenses
        checkCategoryOutlierAgg expense agg

    /// MERCHANT_SPIKE core: `agg` covers the merchant history excluding the
    /// expense itself.
    let checkMerchantSpikeAgg (expense: Expense) (agg: AmountAggregate) =
        if agg.N < 2 then None
        else
            let avg = agg.SumDec / decimal agg.N
            if avg = 0m then None
            elif expense.Amount > avg * 4m then
                let score = min 100 (int ((expense.Amount / avg) * 10m) + 40)
                Some (mkAnomaly expense.Id "MERCHANT_SPIKE" score
                        (sprintf "Unusually large amount for merchant %s." expense.Merchant)
                        "Verify the purchase amount.")
            else None

    /// MERCHANT_SPIKE: unusually large amount versus the merchant's history.
    let checkMerchantSpike (expense: Expense) (history: Expense list) =
        let agg =
            history
            |> List.filter (fun e -> e.Merchant = expense.Merchant && e.Id <> expense.Id)
            |> AmountAggregate.ofExpenses
        checkMerchantSpikeAgg expense agg

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

    let private mkSubHike (expense: Expense) (usualAmount: decimal) =
        if usualAmount > 0m && expense.Amount > usualAmount * 1.2m && expense.Amount - usualAmount > 1m then
            let pct = int (((expense.Amount / usualAmount) - 1m) * 100m)
            Some (mkAnomaly expense.Id "SUB_HIKE" 60
                    (sprintf "Recurring charge at %s is %d%% above its usual %.2M." expense.Merchant pct usualAmount)
                    "Check whether this subscription's price increased.")
        else None

    /// Evaluates the prior-charge aggregates for the SUB_HIKE stability
    /// criteria (mirrors Recurring.detect: 3+ charges over 3+ distinct
    /// months, coefficient of variation <= 0.2). Returns the rounded usual
    /// amount when the pattern is stable.
    let private stableRecurringAmount (n: int) (sum: float) (sumSquares: float) (monthCount: int) =
        if n < Recurring.MinOccurrences || monthCount < Recurring.MinDistinctMonths then None
        else
            let mean = sum / float n
            if mean <= 0.0 then None
            else
                let stdDev = sqrt (max 0.0 (sumSquares / float n - mean * mean))
                if stdDev / mean > Recurring.MaxAmountVariation then None
                else Some (Math.Round(decimal mean, 2))

    /// SUB_HIKE batch form: walks one merchant's charges chronologically,
    /// checking each against the aggregates of the strictly earlier ones -
    /// O(n log n) per merchant instead of O(n) per expense.
    let detectSubscriptionHikes (merchantCharges: Expense list) =
        let byDate =
            merchantCharges
            |> List.sortBy (fun e -> e.Date)
            |> List.groupBy (fun e -> e.Date)
        let mutable n = 0
        let mutable sum = 0.0
        let mutable sumSquares = 0.0
        let mutable months = Set.empty
        let hikes = ResizeArray()
        for _, group in byDate do
            // Same-timestamp charges never see each other as "prior".
            match stableRecurringAmount n sum sumSquares (Set.count months) with
            | Some usual -> for e in group do mkSubHike e usual |> Option.iter hikes.Add
            | None -> ()
            for e in group do
                let v = float e.Amount
                n <- n + 1
                sum <- sum + v
                sumSquares <- sumSquares + v * v
                months <- Set.add (e.Date.Year, e.Date.Month) months
        List.ofSeq hikes

    /// SUB_HIKE: a stable recurring charge (subscription, rent, ...) suddenly
    /// costs 20%+ more than its historical average.
    let checkSubscriptionHike (expense: Expense) (history: Expense list) =
        let mutable n = 0
        let mutable sum = 0.0
        let mutable sumSquares = 0.0
        let mutable months = Set.empty
        for e in history do
            if e.Merchant = expense.Merchant && e.Id <> expense.Id && e.Date < expense.Date then
                let v = float e.Amount
                n <- n + 1
                sum <- sum + v
                sumSquares <- sumSquares + v * v
                months <- Set.add (e.Date.Year, e.Date.Month) months
        stableRecurringAmount n sum sumSquares (Set.count months)
        |> Option.bind (mkSubHike expense)

    /// NIGHT: transaction timestamped deep in the night (1 AM - 4 AM).
    let checkNightSpending (expense: Expense) =
        let hour = expense.Date.Hour
        if hour >= 1 && hour <= 4 then
            Some (mkAnomaly expense.Id "NIGHT" 50
                    "Transaction occurred late at night (1 AM - 4 AM)."
                    "Confirm if you made this purchase.")
        else None
