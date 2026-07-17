module StorageEngineTests

open System
open System.IO
open Xunit
open FinanceAnomalyDetector
open TestHelpers

// All storage-touching tests live in this single module: Storage keeps one
// process-wide connection string, so they must share the same temp database.
let private dbPath = Path.Combine(AppContext.BaseDirectory, sprintf "fad-tests-%O.db" (Guid.NewGuid()))

// Module-level `do` in libraries only runs on first *value* access, which
// xUnit's function-only discovery never triggers - hence an explicit lazy.
let private initDb =
    lazy (
        Storage.configure dbPath
        Storage.initDb ())

let private freshUser () =
    initDb.Force()
    let user = Storage.createUser (sprintf "user_%O" (Guid.NewGuid())) "hash"
    user.Id

[<Fact>]
let ``createUser and getUserByUsername roundtrip`` () =
    initDb.Force()
    let name = sprintf "roundtrip_%O" (Guid.NewGuid())
    let created = Storage.createUser name "somehash"
    match Storage.getUserByUsername name with
    | Some found ->
        Assert.Equal(created.Id, found.Id)
        Assert.Equal("somehash", found.PasswordHash)
    | None -> failwith "user not found"

[<Fact>]
let ``updatePassword persists the new hash`` () =
    let userId = freshUser ()
    Assert.True(Storage.updatePassword userId "newhash")
    Assert.Equal("newhash", (Storage.getUserById userId).Value.PasswordHash)

[<Fact>]
let ``whole-number amounts survive the SQLite roundtrip`` () =
    let userId = freshUser ()
    Storage.insertExpense userId (mkDto 21m "Food" "KFC" (DateTime(2026, 6, 1))) |> ignore
    let stored = Assert.Single(Storage.getAllExpenses userId)
    Assert.Equal(21m, stored.Amount)

[<Fact>]
let ``users only ever see their own data`` () =
    let alice = freshUser ()
    let bob = freshUser ()
    Storage.insertExpense alice (mkDto 10m "Food" "A" (DateTime(2026, 6, 1))) |> ignore
    Storage.insertExpense bob (mkDto 99m "Food" "B" (DateTime(2026, 6, 1))) |> ignore
    let aliceExpenses = Storage.getAllExpenses alice
    Assert.Single(aliceExpenses) |> ignore
    Assert.Equal("A", aliceExpenses.Head.Merchant)

[<Fact>]
let ``queryExpenses paginates with a stable total`` () =
    let userId = freshUser ()
    for i in 1..25 do
        Storage.insertExpense userId (mkDto (decimal i) "Food" "M" (DateTime(2026, 6, 1).AddDays(float i))) |> ignore
    let query = { Page = 2; PageSize = 10; Category = None; Search = None; From = None; To = None }
    let page = Storage.queryExpenses userId query
    Assert.Equal(25, page.Total)
    Assert.Equal(10, page.Items.Length)
    Assert.Equal(2, page.Page)

[<Fact>]
let ``queryExpenses filters by category, search and date range`` () =
    let userId = freshUser ()
    Storage.insertExpense userId (mkDto 10m "Food" "KFC" (DateTime(2026, 6, 1))) |> ignore
    Storage.insertExpense userId (mkDto 20m "Travel" "Uber" (DateTime(2026, 6, 5))) |> ignore
    Storage.insertExpense userId (mkDto 30m "Food" "Cafe Luna" (DateTime(2026, 7, 1))) |> ignore
    let baseQuery = { Page = 1; PageSize = 50; Category = None; Search = None; From = None; To = None }

    Assert.Equal(2, (Storage.queryExpenses userId { baseQuery with Category = Some "Food" }).Total)
    Assert.Equal(1, (Storage.queryExpenses userId { baseQuery with Search = Some "luna" }).Total)
    let june = { baseQuery with From = Some (DateTime(2026, 6, 1)); To = Some (DateTime(2026, 7, 1)) }
    Assert.Equal(2, (Storage.queryExpenses userId june).Total)

[<Fact>]
let ``updateExpense only touches the owner's row`` () =
    let owner = freshUser ()
    let intruder = freshUser ()
    let expense = Storage.insertExpense owner (mkDto 10m "Food" "KFC" (DateTime(2026, 6, 1)))
    Assert.False(Storage.updateExpense intruder expense.Id (mkDto 1m "X" "Y" (DateTime(2026, 6, 1))))
    Assert.True(Storage.updateExpense owner expense.Id (mkDto 15m "Food" "KFC" (DateTime(2026, 6, 1))))
    Assert.Equal(15m, (Storage.getExpenseById owner expense.Id).Value.Amount)

[<Fact>]
let ``deleteExpense removes the expense and its anomalies`` () =
    let userId = freshUser ()
    let expense = Storage.insertExpense userId (mkDto 10m "Food" "KFC" (DateTime(2026, 6, 1, 2, 0, 0)))
    AnomalyEngine.runAll userId |> ignore
    Assert.NotEmpty(Storage.getAnomalies userId) // NIGHT rule fired
    Assert.True(Storage.deleteExpense userId expense.Id)
    Assert.Empty(Storage.getAllExpenses userId)
    Assert.Empty(Storage.getAnomalies userId)

[<Fact>]
let ``engine detects the seeded scenarios end to end`` () =
    let userId = freshUser ()
    // duplicate pair
    Storage.insertExpense userId (mkDto 42.5m "Food" "KFC" (DateTime(2026, 6, 10, 13, 0, 0))) |> ignore
    Storage.insertExpense userId (mkDto 42.5m "Food" "KFC" (DateTime(2026, 6, 10, 13, 4, 0))) |> ignore
    // night purchase
    Storage.insertExpense userId (mkDto 99m "Other" "Bar" (DateTime(2026, 6, 11, 2, 30, 0))) |> ignore
    let detected = AnomalyEngine.runAll userId
    let anomalies = Storage.getAnomalies userId
    Assert.Equal(detected, anomalies.Length)
    Assert.Contains(anomalies, fun a -> a.RuleCode = "DUPLICATE")
    Assert.Contains(anomalies, fun a -> a.RuleCode = "NIGHT")

[<Fact>]
let ``resolved anomalies are never resurrected by a re-run`` () =
    let userId = freshUser ()
    Storage.insertExpense userId (mkDto 42.5m "Food" "KFC" (DateTime(2026, 6, 10, 13, 0, 0))) |> ignore
    Storage.insertExpense userId (mkDto 42.5m "Food" "KFC" (DateTime(2026, 6, 10, 13, 4, 0))) |> ignore
    AnomalyEngine.runAll userId |> ignore

    let duplicate = Storage.getAnomalies userId |> List.find (fun a -> a.RuleCode = "DUPLICATE")
    Assert.True(Storage.resolveAnomaly userId duplicate.Id)

    AnomalyEngine.runAll userId |> ignore
    let after = Storage.getAnomalies userId
    Assert.DoesNotContain(after, fun a -> a.RuleCode = "DUPLICATE")

[<Fact>]
let ``budgets upsert and delete`` () =
    let userId = freshUser ()
    Storage.setBudget userId "Food" 100m
    Storage.setBudget userId "Food" 250m
    let budget = Assert.Single(Storage.getBudgets userId)
    Assert.Equal(250m, budget.LimitAmount)
    Assert.True(Storage.deleteBudget userId "Food")
    Assert.False(Storage.deleteBudget userId "Food")
