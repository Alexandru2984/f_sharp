namespace FinanceAnomalyDetector

open System

/// Pure statistics over in-memory data (unit-testable), with thin
/// Storage-backed wrappers at the bottom used by the HTTP layer.
module Stats =
    type DashboardStats = {
        Currency : string
        TotalExpenses : decimal
        CurrentMonthSpending : decimal
        AverageMonthlySpending : decimal
        AnomalyCount : int
        HighestRiskCategory : string
    }

    type CurrencySummary = {
        Currency : string
        Count : int
        Total : decimal
    }

    /// The currency with the most transactions; "USD" for empty accounts.
    let dominantCurrency (expenses: Expense list) =
        match expenses with
        | [] -> "USD"
        | _ -> expenses |> List.countBy (fun e -> e.Currency) |> List.maxBy snd |> fst

    let computeCurrencies (expenses: Expense list) =
        expenses
        |> List.groupBy (fun e -> e.Currency)
        |> List.map (fun (c, lst) -> { Currency = c; Count = lst.Length; Total = lst |> List.sumBy (fun e -> e.Amount) })
        |> List.sortByDescending (fun c -> c.Count)

    /// Money aggregates are computed for a single currency; the anomaly
    /// signal (count, riskiest category) stays global since risk is not a sum.
    let computeDashboardStats (now: DateTime) (currency: string) (expenses: Expense list) (anomalies: Anomaly list) =
        let money = expenses |> List.filter (fun e -> e.Currency = currency)
        let total = money |> List.sumBy (fun e -> e.Amount)

        let currentMonthSpending =
            money
            |> List.filter (fun e -> e.Date.Month = now.Month && e.Date.Year = now.Year)
            |> List.sumBy (fun e -> e.Amount)

        let months = money |> List.map (fun e -> e.Date.ToString("yyyy-MM")) |> List.distinct |> List.length
        let avgMonthly = if months = 0 then 0m else total / decimal months

        let highestRiskCat =
            let anomalyExpenses = anomalies |> List.choose (fun a -> expenses |> List.tryFind (fun e -> e.Id = a.ExpenseId))
            if anomalyExpenses.IsEmpty then "None"
            else
                anomalyExpenses
                |> List.countBy (fun e -> e.Category)
                |> List.sortByDescending snd
                |> List.head
                |> fst

        { Currency = currency
          TotalExpenses = total
          CurrentMonthSpending = currentMonthSpending
          AverageMonthlySpending = avgMonthly
          AnomalyCount = anomalies.Length
          HighestRiskCategory = highestRiskCat }

    let computeCategoryBreakdown (expenses: Expense list) =
        expenses
        |> List.groupBy (fun e -> e.Category)
        |> List.map (fun (c, lst) -> {| Category = c; Total = lst |> List.sumBy (fun e -> e.Amount) |})
        |> List.sortByDescending (fun x -> x.Total)

    let computeMonthlyTrends (expenses: Expense list) =
        expenses
        |> List.groupBy (fun e -> e.Date.ToString("yyyy-MM"))
        |> List.map (fun (m, lst) -> {| Month = m; Total = lst |> List.sumBy (fun e -> e.Amount) |})
        |> List.sortBy (fun x -> x.Month)

    let computeBudgetStatus (now: DateTime) (budgets: Budget list) (expenses: Expense list) =
        budgets |> List.map (fun b ->
            let spent =
                expenses
                |> List.filter (fun e -> e.Category = b.Category && e.Date.Month = now.Month && e.Date.Year = now.Year)
                |> List.sumBy (fun e -> e.Amount)
            {| Category = b.Category
               Limit = b.LimitAmount
               Spent = spent
               Percentage = if b.LimitAmount = 0m then 0m else Math.Round((spent / b.LimitAmount) * 100m, 1) |})

    type CategoryTotal = {
        Category : string
        Total : decimal
        Share : decimal
    }

    type MerchantTotal = {
        Merchant : string
        Total : decimal
        Count : int
    }

    type MonthlyReport = {
        Month : string
        Currency : string
        Total : decimal
        ExpenseCount : int
        PreviousMonthTotal : decimal
        ChangePercent : decimal
        ByCategory : CategoryTotal list
        TopMerchants : MerchantTotal list
        AnomalyCount : int
    }

    /// Detailed report for one calendar month ("yyyy-MM") in one currency.
    let computeMonthlyReport (month: string) (currency: string) (allExpenses: Expense list) (anomalies: Anomaly list) =
        let expenses = allExpenses |> List.filter (fun e -> e.Currency = currency)
        let inMonth (e: Expense) = e.Date.ToString("yyyy-MM") = month
        let monthExpenses = expenses |> List.filter inMonth
        let total = monthExpenses |> List.sumBy (fun e -> e.Amount)

        let previousMonth =
            match DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None) with
            | true, d -> d.AddMonths(-1).ToString("yyyy-MM")
            | _ -> ""
        let previousTotal =
            expenses
            |> List.filter (fun e -> e.Date.ToString("yyyy-MM") = previousMonth)
            |> List.sumBy (fun e -> e.Amount)

        let changePercent =
            if previousTotal = 0m then 0m
            else Math.Round(((total - previousTotal) / previousTotal) * 100m, 1)

        let byCategory =
            monthExpenses
            |> List.groupBy (fun e -> e.Category)
            |> List.map (fun (c, lst) ->
                let catTotal = lst |> List.sumBy (fun e -> e.Amount)
                { Category = c
                  Total = catTotal
                  Share = if total = 0m then 0m else Math.Round((catTotal / total) * 100m, 1) })
            |> List.sortByDescending (fun x -> x.Total)

        let topMerchants =
            monthExpenses
            |> List.groupBy (fun e -> e.Merchant)
            |> List.map (fun (m, lst) -> { Merchant = m; Total = lst |> List.sumBy (fun e -> e.Amount); Count = lst.Length })
            |> List.sortByDescending (fun x -> x.Total)
            |> List.truncate 5

        let monthExpenseIds = monthExpenses |> List.map (fun e -> e.Id) |> Set.ofList
        let anomalyCount = anomalies |> List.filter (fun a -> Set.contains a.ExpenseId monthExpenseIds) |> List.length

        { Month = month
          Currency = currency
          Total = total
          ExpenseCount = monthExpenses.Length
          PreviousMonthTotal = previousTotal
          ChangePercent = changePercent
          ByCategory = byCategory
          TopMerchants = topMerchants
          AnomalyCount = anomalyCount }

    // ---- Storage-backed wrappers ----
    // `currencyOpt` comes from the ?currency= query param; the user's most
    // used currency is the default.

    let private resolveCurrency (currencyOpt: string option) (expenses: Expense list) =
        currencyOpt |> Option.defaultValue (dominantCurrency expenses)

    let private inCurrency currency (expenses: Expense list) =
        expenses |> List.filter (fun e -> e.Currency = currency)

    let getCurrencies userId =
        computeCurrencies (Storage.getAllExpenses userId)

    let getDashboardStats userId currencyOpt =
        let expenses = Storage.getAllExpenses userId
        computeDashboardStats DateTime.UtcNow (resolveCurrency currencyOpt expenses) expenses (Storage.getAnomalies userId)

    let getCategoryBreakdown userId currencyOpt =
        let expenses = Storage.getAllExpenses userId
        computeCategoryBreakdown (inCurrency (resolveCurrency currencyOpt expenses) expenses)

    let getMonthlyTrends userId currencyOpt =
        let expenses = Storage.getAllExpenses userId
        computeMonthlyTrends (inCurrency (resolveCurrency currencyOpt expenses) expenses)

    let getBudgetStatus userId currencyOpt =
        let expenses = Storage.getAllExpenses userId
        computeBudgetStatus DateTime.UtcNow (Storage.getBudgets userId) (inCurrency (resolveCurrency currencyOpt expenses) expenses)

    let getMonthlyReport userId (month: string) currencyOpt =
        let expenses = Storage.getAllExpenses userId
        computeMonthlyReport month (resolveCurrency currencyOpt expenses) expenses (Storage.getAnomalies userId)
