module StatsTests

open System
open Xunit
open FinanceAnomalyDetector
open TestHelpers

let private now = DateTime(2026, 6, 15)

let private sample =
    [ mkExpense 1 100m "Food" "KFC" (DateTime(2026, 5, 10))
      mkExpense 2 50m "Food" "KFC" (DateTime(2026, 6, 1))
      mkExpense 3 200m "Rent" "Landlord" (DateTime(2026, 6, 1))
      mkExpense 4 30m "Food" "Cafe" (DateTime(2026, 6, 12)) ]

[<Fact>]
let ``dashboard stats aggregate totals and current month`` () =
    let stats = Stats.computeDashboardStats now sample []
    Assert.Equal(380m, stats.TotalExpenses)
    Assert.Equal(280m, stats.CurrentMonthSpending)
    Assert.Equal(190m, stats.AverageMonthlySpending) // 380 across 2 distinct months
    Assert.Equal("None", stats.HighestRiskCategory)

[<Fact>]
let ``highest risk category follows anomaly counts`` () =
    let anomalies =
        [ AnomalyRules.mkAnomaly 2 "NIGHT" 50 "r" "rec"
          AnomalyRules.mkAnomaly 4 "NIGHT" 50 "r" "rec" ]
    let stats = Stats.computeDashboardStats now sample anomalies
    Assert.Equal("Food", stats.HighestRiskCategory)
    Assert.Equal(2, stats.AnomalyCount)

[<Fact>]
let ``monthly trends group and sort by month`` () =
    let trends = Stats.computeMonthlyTrends sample
    Assert.Equal(2, trends.Length)
    Assert.Equal("2026-05", trends.[0].Month)
    Assert.Equal(100m, trends.[0].Total)
    Assert.Equal(280m, trends.[1].Total)

[<Fact>]
let ``category breakdown sums per category, largest first`` () =
    let breakdown = Stats.computeCategoryBreakdown sample
    Assert.Equal("Rent", breakdown.[0].Category)
    Assert.Equal(200m, breakdown.[0].Total)
    Assert.Equal(180m, (breakdown |> List.find (fun c -> c.Category = "Food")).Total)

[<Fact>]
let ``budget status computes spent and percentage for the current month`` () =
    let budgets = [ { UserId = 1; Category = "Food"; LimitAmount = 100m } ]
    let status = Stats.computeBudgetStatus now budgets sample
    let food = Assert.Single(status)
    Assert.Equal(80m, food.Spent) // only June's Food expenses
    Assert.Equal(80m, food.Percentage)

[<Fact>]
let ``zero-limit budgets do not divide by zero`` () =
    let budgets = [ { UserId = 1; Category = "Food"; LimitAmount = 0m } ]
    let status = Stats.computeBudgetStatus now budgets sample
    Assert.Equal(0m, (Assert.Single status).Percentage)

[<Fact>]
let ``monthly report computes totals, shares and month-over-month change`` () =
    let report = Stats.computeMonthlyReport "2026-06" sample []
    Assert.Equal(280m, report.Total)
    Assert.Equal(3, report.ExpenseCount)
    Assert.Equal(100m, report.PreviousMonthTotal)
    Assert.Equal(180m, report.ChangePercent) // 100 -> 280
    let rent = report.ByCategory |> List.find (fun c -> c.Category = "Rent")
    Assert.Equal(71.4m, rent.Share)
    Assert.Equal("Landlord", report.TopMerchants.Head.Merchant)

[<Fact>]
let ``monthly report counts only anomalies on that month's expenses`` () =
    let anomalies =
        [ AnomalyRules.mkAnomaly 1 "NIGHT" 50 "r" "rec"   // May expense
          AnomalyRules.mkAnomaly 3 "NIGHT" 50 "r" "rec" ] // June expense
    let report = Stats.computeMonthlyReport "2026-06" sample anomalies
    Assert.Equal(1, report.AnomalyCount)

[<Fact>]
let ``report for an empty month is all zeros`` () =
    let report = Stats.computeMonthlyReport "2027-01" sample []
    Assert.Equal(0m, report.Total)
    Assert.Equal(0, report.ExpenseCount)
    Assert.Empty(report.ByCategory)
