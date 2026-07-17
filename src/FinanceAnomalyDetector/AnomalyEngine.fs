namespace FinanceAnomalyDetector

module AnomalyEngine =
    open AnomalyRules

    let rules : (Expense -> Expense list -> Anomaly option) list = [
        checkCategoryOutlier
        checkMerchantSpike
        checkCategoryChange
        checkDuplicate
        checkSubscriptionHike
    ]

    let runForExpense (expense: Expense) (history: Expense list) =
        let contextual = rules |> List.choose (fun rule -> rule expense history)
        match checkNightSpending expense with
        | Some a -> a :: contextual
        | None -> contextual

    /// Re-evaluates every rule for the user's full history. Findings the user
    /// already resolved stay resolved: a fresh anomaly matching a resolved
    /// (ExpenseId, RuleCode) pair - or a pre-RuleCode 'LEGACY' resolution for
    /// the same expense - is suppressed instead of resurrected.
    let runAll userId =
        let expenses = Storage.getAllExpenses userId
        let resolvedKeys = Storage.getResolvedAnomalyKeys userId |> Set.ofList
        let isSuppressed (a: Anomaly) =
            Set.contains (a.ExpenseId, a.RuleCode) resolvedKeys
            || Set.contains (a.ExpenseId, "LEGACY") resolvedKeys
        let fresh =
            expenses
            |> List.collect (fun e -> runForExpense e expenses)
            |> List.filter (fun a -> not (isSuppressed a))
        Storage.replaceUnresolvedAnomalies userId fresh
        fresh.Length
