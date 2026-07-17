module RecurringTests

open System
open Xunit
open FinanceAnomalyDetector
open TestHelpers

let private monthly (amounts: decimal list) merchant =
    amounts |> List.mapi (fun i a -> mkExpense (i + 1) a "Entertainment" merchant (DateTime(2026, i + 1, 5)))

[<Fact>]
let ``three stable monthly charges are detected as recurring`` () =
    let detected = Recurring.detect (monthly [15.99m; 15.99m; 15.99m] "Netflix")
    let r = Assert.Single(detected)
    Assert.Equal("Netflix", r.Merchant)
    Assert.Equal(15.99m, r.AverageAmount)
    Assert.Equal(3, r.MonthsActive)

[<Fact>]
let ``two charges are not enough`` () =
    Assert.Empty(Recurring.detect (monthly [15.99m; 15.99m] "Netflix"))

[<Fact>]
let ``three charges inside the same month are not recurring`` () =
    let sameMonth =
        [1; 2; 3] |> List.map (fun d -> mkExpense d 15.99m "Entertainment" "Netflix" (DateTime(2026, 5, d)))
    Assert.Empty(Recurring.detect sameMonth)

[<Fact>]
let ``unstable amounts are not recurring`` () =
    Assert.Empty(Recurring.detect (monthly [10m; 55m; 120m] "Store"))

[<Fact>]
let ``small variation within tolerance still counts`` () =
    let detected = Recurring.detect (monthly [50m; 52m; 49m] "Gym")
    Assert.Single(detected) |> ignore

[<Fact>]
let ``category is the most frequent one for the merchant`` () =
    let expenses =
        [ mkExpense 1 30m "Utilities" "PowerCo" (DateTime(2026, 1, 1))
          mkExpense 2 30m "Utilities" "PowerCo" (DateTime(2026, 2, 1))
          mkExpense 3 30m "Bills" "PowerCo" (DateTime(2026, 3, 1)) ]
    let r = Assert.Single(Recurring.detect expenses)
    Assert.Equal("Utilities", r.Category)
