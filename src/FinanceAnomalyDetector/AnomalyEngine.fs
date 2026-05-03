namespace FinanceAnomalyDetector

open System

module AnomalyEngine =
    open AnomalyRules
    
    let rules = [
        checkCategoryAverage
        checkMerchantSpike
        checkCategoryChange
    ]

    let runForExpense (expense: Expense) (history: Expense list) =
        let results = 
            rules 
            |> List.choose (fun rule -> rule expense history)
            |> List.append (match checkNightSpending expense with | Some a -> [a] | None -> [])
        
        results
        
    let runAll () =
        Storage.initDb()
        
        let conn = new Microsoft.Data.Sqlite.SqliteConnection(Storage.connectionString)
        conn.Open()
        let cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM Anomalies"
        cmd.ExecuteNonQuery() |> ignore
        
        let expenses = Storage.getAllExpenses()
        let mutable detected = 0
        for exp in expenses do
            let anomalies = runForExpense exp expenses
            for a in anomalies do
                Storage.insertAnomaly a
                detected <- detected + 1
        detected
