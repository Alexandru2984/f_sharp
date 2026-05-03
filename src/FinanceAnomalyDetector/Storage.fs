namespace FinanceAnomalyDetector

open System
open Microsoft.Data.Sqlite
open Dapper

module Storage =
    let connectionString = "Data Source=/home/micu/f_sharp/data/finance.db"

    let initDb () =
        use conn = new SqliteConnection(connectionString)
        conn.Open()
        let cmd = conn.CreateCommand()
        cmd.CommandText <- """
            CREATE TABLE IF NOT EXISTS Expenses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Amount DECIMAL NOT NULL,
                Currency TEXT NOT NULL,
                Category TEXT NOT NULL,
                Merchant TEXT NOT NULL,
                Description TEXT,
                Date DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Anomalies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExpenseId INTEGER NOT NULL,
                Score INTEGER NOT NULL,
                Severity TEXT NOT NULL,
                Reason TEXT NOT NULL,
                Recommendation TEXT NOT NULL,
                DetectedAt DATETIME NOT NULL,
                IsResolved BOOLEAN NOT NULL DEFAULT 0,
                FOREIGN KEY(ExpenseId) REFERENCES Expenses(Id)
            );
            CREATE TABLE IF NOT EXISTS Budgets (
                Category TEXT PRIMARY KEY,
                LimitAmount DECIMAL NOT NULL
            );
        """
        cmd.ExecuteNonQuery() |> ignore

    let insertExpense (expense: ExpenseDto) =
        use conn = new SqliteConnection(connectionString)
        let sql = """
            INSERT INTO Expenses (Amount, Currency, Category, Merchant, Description, Date, CreatedAt)
            VALUES (@Amount, @Currency, @Category, @Merchant, @Description, @Date, @CreatedAt);
            SELECT last_insert_rowid();
        """
        let id = conn.QuerySingle<int>(sql, {| Amount = expense.Amount; Currency = expense.Currency; Category = expense.Category; Merchant = expense.Merchant; Description = expense.Description; Date = expense.Date; CreatedAt = DateTime.UtcNow |})
        { Id = id
          Amount = expense.Amount
          Currency = expense.Currency
          Category = expense.Category
          Merchant = expense.Merchant
          Description = expense.Description
          Date = expense.Date
          CreatedAt = DateTime.UtcNow }

    let getExpenses () =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses ORDER BY Date DESC LIMIT 100") |> List.ofSeq

    let getAllExpenses () =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses") |> List.ofSeq

    let insertAnomaly (anomaly: Anomaly) =
        use conn = new SqliteConnection(connectionString)
        let sql = """
            INSERT INTO Anomalies (ExpenseId, Score, Severity, Reason, Recommendation, DetectedAt, IsResolved)
            VALUES (@ExpenseId, @Score, @Severity, @Reason, @Recommendation, @DetectedAt, @IsResolved);
        """
        conn.Execute(sql, anomaly) |> ignore

    let resolveAnomaly (id: int) =
        use conn = new SqliteConnection(connectionString)
        let sql = "UPDATE Anomalies SET IsResolved = 1 WHERE Id = @Id"
        conn.Execute(sql, {| Id = id |}) |> ignore

    let getAnomalies () =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Anomaly>("SELECT * FROM Anomalies WHERE IsResolved = 0 ORDER BY DetectedAt DESC LIMIT 100") |> List.ofSeq

    let setBudget (category: string) (limit: decimal) =
        use conn = new SqliteConnection(connectionString)
        let sql = "INSERT OR REPLACE INTO Budgets (Category, LimitAmount) VALUES (@Category, @LimitAmount)"
        conn.Execute(sql, {| Category = category; LimitAmount = limit |}) |> ignore

    let getBudgets () =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Budget>("SELECT * FROM Budgets") |> List.ofSeq
