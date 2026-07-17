namespace FinanceAnomalyDetector

open System

/// Detection of recurring charges (subscriptions, rent, utilities):
/// same merchant, stable amount, spread over several distinct months.
module Recurring =
    type RecurringTransaction = {
        Merchant : string
        Category : string
        AverageAmount : decimal
        Occurrences : int
        MonthsActive : int
        FirstDate : DateTime
        LastDate : DateTime
    }

    [<Literal>]
    let MinOccurrences = 3

    [<Literal>]
    let MinDistinctMonths = 3

    /// Maximum coefficient of variation for amounts to count as "stable".
    [<Literal>]
    let MaxAmountVariation = 0.2

    let detect (expenses: Expense list) =
        expenses
        |> List.groupBy (fun e -> e.Merchant)
        |> List.choose (fun (merchant, charges) ->
            let months = charges |> List.map (fun e -> e.Date.Year, e.Date.Month) |> List.distinct
            if charges.Length < MinOccurrences || months.Length < MinDistinctMonths then None
            else
                let amounts = charges |> List.map (fun e -> float e.Amount)
                let mean = List.average amounts
                if mean <= 0.0 then None
                else
                    let sumSquares = amounts |> List.sumBy (fun a -> (a - mean) ** 2.0)
                    let stdDev = sqrt (sumSquares / float amounts.Length)
                    if stdDev / mean > MaxAmountVariation then None
                    else
                        let category = charges |> List.countBy (fun e -> e.Category) |> List.maxBy snd |> fst
                        Some { Merchant = merchant
                               Category = category
                               AverageAmount = Math.Round(decimal mean, 2)
                               Occurrences = charges.Length
                               MonthsActive = months.Length
                               FirstDate = charges |> List.map (fun e -> e.Date) |> List.min
                               LastDate = charges |> List.map (fun e -> e.Date) |> List.max })
        |> List.sortByDescending (fun r -> r.AverageAmount)
