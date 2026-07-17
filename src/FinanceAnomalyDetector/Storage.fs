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
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT UNIQUE NOT NULL,
                PasswordHash TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Expenses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Amount DECIMAL NOT NULL,
                Currency TEXT NOT NULL,
                Category TEXT NOT NULL,
                Merchant TEXT NOT NULL,
                Description TEXT,
                Date DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL,
                FOREIGN KEY(UserId) REFERENCES Users(Id)
            );
            CREATE TABLE IF NOT EXISTS Anomalies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                ExpenseId INTEGER NOT NULL,
                Score INTEGER NOT NULL,
                Severity TEXT NOT NULL,
                Reason TEXT NOT NULL,
                Recommendation TEXT NOT NULL,
                DetectedAt DATETIME NOT NULL,
                IsResolved BOOLEAN NOT NULL DEFAULT 0,
                FOREIGN KEY(UserId) REFERENCES Users(Id),
                FOREIGN KEY(ExpenseId) REFERENCES Expenses(Id)
            );
            CREATE TABLE IF NOT EXISTS Budgets (
                UserId INTEGER NOT NULL,
                Category TEXT NOT NULL,
                LimitAmount DECIMAL NOT NULL,
                PRIMARY KEY(UserId, Category),
                FOREIGN KEY(UserId) REFERENCES Users(Id)
            );
        """
        cmd.ExecuteNonQuery() |> ignore

    let insertExpense userId (expense: ExpenseDto) =
        use conn = new SqliteConnection(connectionString)
        let sql = """
            INSERT INTO Expenses (UserId, Amount, Currency, Category, Merchant, Description, Date, CreatedAt)
            VALUES (@UserId, @Amount, @Currency, @Category, @Merchant, @Description, @Date, @CreatedAt);
            SELECT last_insert_rowid();
        """
        let id = conn.QuerySingle<int>(sql, {| UserId = userId; Amount = expense.Amount; Currency = expense.Currency; Category = expense.Category; Merchant = expense.Merchant; Description = expense.Description; Date = expense.Date; CreatedAt = DateTime.UtcNow |})
        { Id = id
          UserId = userId
          Amount = expense.Amount
          Currency = expense.Currency
          Category = expense.Category
          Merchant = expense.Merchant
          Description = expense.Description
          Date = expense.Date
          CreatedAt = DateTime.UtcNow }

    let getExpenses userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses WHERE UserId = @UserId ORDER BY Date DESC LIMIT 100", {| UserId = userId |}) |> List.ofSeq

    let getAllExpenses userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses WHERE UserId = @UserId", {| UserId = userId |}) |> List.ofSeq

    let insertAnomaly userId (anomaly: Anomaly) =
        use conn = new SqliteConnection(connectionString)
        let sql = """
            INSERT INTO Anomalies (UserId, ExpenseId, Score, Severity, Reason, Recommendation, DetectedAt, IsResolved)
            VALUES (@UserId, @ExpenseId, @Score, @Severity, @Reason, @Recommendation, @DetectedAt, @IsResolved);
        """
        conn.Execute(sql, {| anomaly with UserId = userId |}) |> ignore

    let resolveAnomaly userId (id: int) =
        use conn = new SqliteConnection(connectionString)
        let sql = "UPDATE Anomalies SET IsResolved = 1 WHERE Id = @Id AND UserId = @UserId"
        conn.Execute(sql, {| Id = id; UserId = userId |}) |> ignore

    let getAnomalies userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Anomaly>("SELECT * FROM Anomalies WHERE UserId = @UserId AND IsResolved = 0 ORDER BY DetectedAt DESC LIMIT 100", {| UserId = userId |}) |> List.ofSeq

    let setBudget userId (category: string) (limit: decimal) =
        use conn = new SqliteConnection(connectionString)
        let sql = "INSERT OR REPLACE INTO Budgets (UserId, Category, LimitAmount) VALUES (@UserId, @Category, @LimitAmount)"
        conn.Execute(sql, {| UserId = userId; Category = category; LimitAmount = limit |}) |> ignore

    let getBudgets userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Budget>("SELECT * FROM Budgets WHERE UserId = @UserId", {| UserId = userId |}) |> List.ofSeq

    let getUserByUsername (username: string) =
        use conn = new SqliteConnection(connectionString)
        conn.Query<User>("SELECT * FROM Users WHERE Username = @Username", {| Username = username |}) |> Seq.tryHead

    let createUser (username: string) (passwordHash: string) =
        use conn = new SqliteConnection(connectionString)
        let sql = "INSERT INTO Users (Username, PasswordHash, CreatedAt) VALUES (@Username, @PasswordHash, @CreatedAt); SELECT last_insert_rowid();"
        let id = conn.QuerySingle<int>(sql, {| Username = username; PasswordHash = passwordHash; CreatedAt = DateTime.UtcNow |})
        { Id = id; Username = username; PasswordHash = passwordHash; CreatedAt = DateTime.UtcNow }

    /// Seeds an admin account only when ADMIN_USERNAME and ADMIN_PASSWORD are
    /// provided via environment; never ships a hardcoded default credential.
    let seedAdmin () =
        let username = Environment.GetEnvironmentVariable("ADMIN_USERNAME")
        let password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
        if not (String.IsNullOrWhiteSpace username) && not (String.IsNullOrWhiteSpace password) then
            match getUserByUsername username with
            | None ->
                let hash = BCrypt.Net.BCrypt.HashPassword(password)
                createUser username hash |> ignore
            | Some _ -> ()
