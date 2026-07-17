namespace FinanceAnomalyDetector

open System

/// Pure statistics over in-memory data (unit-testable), with thin
/// Storage-backed wrappers at the bottom used by the HTTP layer.
module Stats =
    type DashboardStats = {
        TotalExpenses : decimal
        CurrentMonthSpending : decimal
        AverageMonthlySpending : decimal
        AnomalyCount : int
        HighestRiskCategory : string
    }

    let computeDashboardStats (now: DateTime) (expenses: Expense list) (anomalies: Anomaly list) =
        let total = expenses |> List.sumBy (fun e -> e.Amount)

        let currentMonthSpending =
            expenses
            |> List.filter (fun e -> e.Date.Month = now.Month && e.Date.Year = now.Year)
            |> List.sumBy (fun e -> e.Amount)

        let months = expenses |> List.map (fun e -> e.Date.ToString("yyyy-MM")) |> List.distinct |> List.length
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

        { TotalExpenses = total
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

    // ---- Storage-backed wrappers ----

    let getDashboardStats userId =
        computeDashboardStats DateTime.UtcNow (Storage.getAllExpenses userId) (Storage.getAnomalies userId)

    let getCategoryBreakdown userId =
        computeCategoryBreakdown (Storage.getAllExpenses userId)

    let getMonthlyTrends userId =
        computeMonthlyTrends (Storage.getAllExpenses userId)

    let getBudgetStatus userId =
        computeBudgetStatus DateTime.UtcNow (Storage.getBudgets userId) (Storage.getAllExpenses userId)
