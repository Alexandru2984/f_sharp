module AnomalyRulesTests

open System
open Xunit
open FinanceAnomalyDetector
open TestHelpers

// ---- NIGHT ----

[<Theory>]
[<InlineData(1, true)>]
[<InlineData(4, true)>]
[<InlineData(0, false)>]
[<InlineData(5, false)>]
[<InlineData(13, false)>]
let ``night rule fires only between 1 and 4 AM`` (hour: int) (expected: bool) =
    let e = mkExpense 1 50m "Other" "Bar" (DateTime(2026, 6, 1, hour, 30, 0))
    Assert.Equal(expected, (AnomalyRules.checkNightSpending e).IsSome)

// ---- DUPLICATE ----

[<Fact>]
let ``duplicate charge within 10 minutes flags only the later expense`` () =
    let first = mkExpense 1 42.5m "Food" "KFC" (DateTime(2026, 6, 1, 13, 0, 0))
    let second = mkExpense 2 42.5m "Food" "KFC" (DateTime(2026, 6, 1, 13, 4, 0))
    let history = [first; second]
    let flagged = AnomalyRules.checkDuplicate second history
    Assert.True(flagged.IsSome)
    Assert.Equal("DUPLICATE", flagged.Value.RuleCode)
    Assert.True((AnomalyRules.checkDuplicate first history).IsNone)

[<Fact>]
let ``different amount or wide gap is not a duplicate`` () =
    let first = mkExpense 1 42.5m "Food" "KFC" (DateTime(2026, 6, 1, 13, 0, 0))
    let laterAmount = mkExpense 2 43m "Food" "KFC" (DateTime(2026, 6, 1, 13, 4, 0))
    let laterTime = mkExpense 3 42.5m "Food" "KFC" (DateTime(2026, 6, 1, 13, 20, 0))
    Assert.True((AnomalyRules.checkDuplicate laterAmount [first; laterAmount]).IsNone)
    Assert.True((AnomalyRules.checkDuplicate laterTime [first; laterTime]).IsNone)

// ---- CAT_OUTLIER ----

let private groceriesHistory =
    [ for i in 1..6 -> mkExpense i (decimal (20 + i)) "Groceries" "Lidl" (DateTime(2026, 6, i, 12, 0, 0)) ]

[<Fact>]
let ``z-score outlier fires on a large category spike`` () =
    let spike = mkExpense 99 900m "Groceries" "Lidl" (DateTime(2026, 6, 20, 12, 0, 0))
    let result = AnomalyRules.checkCategoryOutlier spike (spike :: groceriesHistory)
    Assert.True(result.IsSome)
    Assert.Equal("CAT_OUTLIER", result.Value.RuleCode)
    Assert.Equal("critical", result.Value.Severity)

[<Fact>]
let ``a normal amount within the category does not fire`` () =
    let normal = mkExpense 99 25m "Groceries" "Lidl" (DateTime(2026, 6, 20, 12, 0, 0))
    Assert.True((AnomalyRules.checkCategoryOutlier normal (normal :: groceriesHistory)).IsNone)

[<Fact>]
let ``small samples fall back to the 3x-average heuristic`` () =
    let history =
        [ for i in 1..3 -> mkExpense i 20m "Travel" "Uber" (DateTime(2026, 6, i)) ]
    let spike = mkExpense 99 100m "Travel" "Uber" (DateTime(2026, 6, 20))
    let result = AnomalyRules.checkCategoryOutlier spike (spike :: history)
    Assert.True(result.IsSome)
    let mild = mkExpense 98 40m "Travel" "Uber" (DateTime(2026, 6, 21))
    Assert.True((AnomalyRules.checkCategoryOutlier mild (mild :: history)).IsNone)

[<Fact>]
let ``fewer than three data points never fire`` () =
    let history = [ mkExpense 1 20m "Pets" "Vet" (DateTime(2026, 6, 1)) ]
    let spike = mkExpense 99 900m "Pets" "Vet" (DateTime(2026, 6, 20))
    Assert.True((AnomalyRules.checkCategoryOutlier spike (spike :: history)).IsNone)

// ---- MERCHANT_SPIKE ----

[<Fact>]
let ``merchant spike fires above 4x the merchant average`` () =
    let history =
        [ mkExpense 1 10m "Food" "Cafe" (DateTime(2026, 6, 1))
          mkExpense 2 12m "Food" "Cafe" (DateTime(2026, 6, 2)) ]
    let spike = mkExpense 99 100m "Food" "Cafe" (DateTime(2026, 6, 20))
    let result = AnomalyRules.checkMerchantSpike spike (spike :: history)
    Assert.True(result.IsSome)
    Assert.Equal("MERCHANT_SPIKE", result.Value.RuleCode)

// ---- CAT_CHANGE ----

[<Fact>]
let ``category change fires when a merchant switches category`` () =
    let history =
        [ for i in 1..3 -> mkExpense i 30m "Groceries" "Amazon" (DateTime(2026, 6, i)) ]
    let odd = mkExpense 99 30m "Electronics" "Amazon" (DateTime(2026, 6, 20))
    let result = AnomalyRules.checkCategoryChange odd (odd :: history)
    Assert.True(result.IsSome)
    Assert.Equal("CAT_CHANGE", result.Value.RuleCode)

[<Fact>]
let ``consistent category does not fire`` () =
    let history =
        [ for i in 1..3 -> mkExpense i 30m "Groceries" "Amazon" (DateTime(2026, 6, i)) ]
    let same = mkExpense 99 30m "Groceries" "Amazon" (DateTime(2026, 6, 20))
    Assert.True((AnomalyRules.checkCategoryChange same (same :: history)).IsNone)

// ---- SUB_HIKE ----

let private netflixHistory =
    [ for m in 3..5 -> mkExpense m 15.99m "Entertainment" "Netflix" (DateTime(2026, m, 5)) ]

[<Fact>]
let ``subscription hike fires when a recurring charge jumps 20 percent`` () =
    let hiked = mkExpense 99 19.99m "Entertainment" "Netflix" (DateTime(2026, 6, 5))
    let result = AnomalyRules.checkSubscriptionHike hiked (hiked :: netflixHistory)
    Assert.True(result.IsSome)
    Assert.Equal("SUB_HIKE", result.Value.RuleCode)

[<Fact>]
let ``an unchanged subscription price does not fire`` () =
    let same = mkExpense 99 15.99m "Entertainment" "Netflix" (DateTime(2026, 6, 5))
    Assert.True((AnomalyRules.checkSubscriptionHike same (same :: netflixHistory)).IsNone)

// ---- severity mapping ----

[<Theory>]
[<InlineData(95, "critical")>]
[<InlineData(75, "high")>]
[<InlineData(50, "medium")>]
[<InlineData(10, "low")>]
let ``severity thresholds`` (score: int) (expected: string) =
    Assert.Equal(expected, AnomalyRules.getSeverity score)
