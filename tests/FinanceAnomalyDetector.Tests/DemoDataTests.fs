module DemoDataTests

open System
open Xunit
open FinanceAnomalyDetector

let private now = DateTime(2026, 7, 17, 12, 0, 0)
let private sample = DemoData.generate 1 now

let private asExpenses (dtos: ExpenseDto list) =
    dtos |> List.mapi (fun i d ->
        { Id = i + 1; UserId = 1; Amount = d.Amount; Currency = d.Currency; Category = d.Category
          Merchant = d.Merchant; Description = d.Description; Date = d.Date; CreatedAt = now })

[<Fact>]
let ``demo dataset is substantial and spans five months`` () =
    Assert.True(sample.Length > 60, sprintf "got %d rows" sample.Length)
    let months = sample |> List.map (fun e -> e.Date.ToString("yyyy-MM")) |> List.distinct
    Assert.Equal(5, months.Length)

[<Fact>]
let ``demo dataset passes expense validation`` () =
    for dto in sample do
        match Validation.validateExpense dto with
        | Ok _ -> ()
        | Error errs -> failwithf "invalid demo row %A: %A" dto errs

[<Fact>]
let ``demo dataset includes a second currency`` () =
    let currencies = sample |> List.map (fun e -> e.Currency) |> List.distinct
    Assert.Contains("USD", currencies)
    Assert.Contains("EUR", currencies)

[<Fact>]
let ``demo dataset is deterministic per seed`` () =
    Assert.Equal<ExpenseDto list>(DemoData.generate 7 now, DemoData.generate 7 now)

[<Fact>]
let ``engine finds every showcase scenario in the demo data`` () =
    let detected = AnomalyEngine.detectAll (asExpenses sample)
    let codes = detected |> List.map (fun a -> a.RuleCode) |> Set.ofList
    for expected in ["DUPLICATE"; "NIGHT"; "SUB_HIKE"; "CAT_OUTLIER"] do
        Assert.True(Set.contains expected codes, sprintf "missing %s in %A" expected codes)
