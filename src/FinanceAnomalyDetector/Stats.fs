namespace FinanceAnomalyDetector

open System

module Stats =
    type DashboardStats = {
        TotalExpenses : decimal
        CurrentMonthSpending : decimal
        AverageMonthlySpending : decimal
        AnomalyCount : int
        HighestRiskCategory : string
    }

    let getDashboardStats () =
        let expenses = Storage.getAllExpenses()
        let anomalies = Storage.getAnomalies()
        
        let total = expenses |> List.sumBy (fun e -> e.Amount)
        
        let currentMonth = DateTime.UtcNow.Month
        let currentYear = DateTime.UtcNow.Year
        let currentMonthSpending = 
            expenses 
            |> List.filter (fun e -> e.Date.Month = currentMonth && e.Date.Year = currentYear) 
            |> List.sumBy (fun e -> e.Amount)
            
        let months = expenses |> List.map (fun e -> e.Date.ToString("yyyy-MM")) |> List.distinct |> List.length
        let avgMonthly = if months = 0 then 0m else total / decimal months
        
        let anomalyCount = anomalies.Length
        
        let highestRiskCat = 
            if anomalies.IsEmpty then "None"
            else
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
          AnomalyCount = anomalyCount
          HighestRiskCategory = highestRiskCat }
          
    let getCategoryBreakdown () =
        let expenses = Storage.getAllExpenses()
        expenses |> List.groupBy (fun e -> e.Category) |> List.map (fun (c, lst) -> {| Category = c; Total = lst |> List.sumBy (fun e -> e.Amount) |})
