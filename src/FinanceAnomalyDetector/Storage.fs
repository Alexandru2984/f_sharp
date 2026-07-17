namespace FinanceAnomalyDetector

open System
open Microsoft.Data.Sqlite
open Dapper

/// SQLite has no native decimal type: whole amounts come back as Int64 and
/// fractional ones as Double, either of which breaks Dapper's default strict
/// cast to decimal. This handler accepts whatever affinity SQLite chose.
type private SqliteDecimalHandler() =
    inherit SqlMapper.TypeHandler<decimal>()
    override _.SetValue(param, value) = param.Value <- value
    override _.Parse(value: obj) =
        match value with
        | :? decimal as d -> d
        | :? double as d -> decimal d
        | :? int64 as i -> decimal i
        | :? string as s -> Convert.ToDecimal(s, System.Globalization.CultureInfo.InvariantCulture)
        | other -> Convert.ToDecimal(other, System.Globalization.CultureInfo.InvariantCulture)

module Storage =
    do SqlMapper.AddTypeHandler(SqliteDecimalHandler())

    /// Set at startup via configure(); tests point this at a throwaway file.
    let mutable connectionString = "Data Source=data/finance.db;Foreign Keys=True"

    let configure (dbPath: string) =
        let dir = System.IO.Path.GetDirectoryName(dbPath: string)
        if not (String.IsNullOrEmpty dir) then
            System.IO.Directory.CreateDirectory(dir) |> ignore
        connectionString <- sprintf "Data Source=%s;Foreign Keys=True" dbPath

    let private columnExists (conn: SqliteConnection) (table: string) (column: string) =
        conn.Query<string>(sprintf "SELECT name FROM pragma_table_info('%s')" table)
        |> Seq.exists (fun c -> String.Equals(c, column, StringComparison.OrdinalIgnoreCase))

    let initDb () =
        use conn = new SqliteConnection(connectionString)
        conn.Open()
        let cmd = conn.CreateCommand()
        cmd.CommandText <- """
            PRAGMA journal_mode=WAL;
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
            CREATE INDEX IF NOT EXISTS IX_Expenses_UserId_Date ON Expenses(UserId, Date);
            CREATE INDEX IF NOT EXISTS IX_Expenses_UserId_Category ON Expenses(UserId, Category);
            CREATE INDEX IF NOT EXISTS IX_Anomalies_UserId_Resolved ON Anomalies(UserId, IsResolved);
        """
        cmd.ExecuteNonQuery() |> ignore

        // Lightweight migration for databases created before RuleCode existed.
        if not (columnExists conn "Anomalies" "RuleCode") then
            conn.Execute("ALTER TABLE Anomalies ADD COLUMN RuleCode TEXT NOT NULL DEFAULT 'LEGACY'") |> ignore

    let private insertExpenseSql = """
        INSERT INTO Expenses (UserId, Amount, Currency, Category, Merchant, Description, Date, CreatedAt)
        VALUES (@UserId, @Amount, @Currency, @Category, @Merchant, @Description, @Date, @CreatedAt);
        SELECT last_insert_rowid();
    """

    let insertExpense userId (expense: ExpenseDto) =
        use conn = new SqliteConnection(connectionString)
        let now = DateTime.UtcNow
        let id = conn.QuerySingle<int>(insertExpenseSql, {| UserId = userId; Amount = expense.Amount; Currency = expense.Currency; Category = expense.Category; Merchant = expense.Merchant; Description = expense.Description; Date = expense.Date; CreatedAt = now |})
        { Id = id
          UserId = userId
          Amount = expense.Amount
          Currency = expense.Currency
          Category = expense.Category
          Merchant = expense.Merchant
          Description = expense.Description
          Date = expense.Date
          CreatedAt = now }

    /// Inserts a batch of expenses inside a single transaction; returns the row count.
    let insertExpenses userId (expenses: ExpenseDto list) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()
        use tx = conn.BeginTransaction()
        let now = DateTime.UtcNow
        for e in expenses do
            conn.Execute(insertExpenseSql, {| UserId = userId; Amount = e.Amount; Currency = e.Currency; Category = e.Category; Merchant = e.Merchant; Description = e.Description; Date = e.Date; CreatedAt = now |}, tx) |> ignore
        tx.Commit()
        expenses.Length

    let getAllExpenses userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses WHERE UserId = @UserId", {| UserId = userId |}) |> List.ofSeq

    let getExpenseById userId (id: int) =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Expense>("SELECT * FROM Expenses WHERE UserId = @UserId AND Id = @Id", {| UserId = userId; Id = id |}) |> Seq.tryHead

    /// Paginated, filtered expense listing. Filters are combined with AND;
    /// search matches merchant or description as a substring.
    let queryExpenses userId (query: ExpenseQuery) =
        use conn = new SqliteConnection(connectionString)
        let conditions = System.Text.StringBuilder("WHERE UserId = @UserId")
        let parameters = DynamicParameters()
        parameters.Add("UserId", userId)
        query.Category |> Option.iter (fun c ->
            conditions.Append(" AND Category = @Category") |> ignore
            parameters.Add("Category", c))
        query.Search |> Option.iter (fun s ->
            conditions.Append(" AND (Merchant LIKE @Search OR Description LIKE @Search)") |> ignore
            parameters.Add("Search", "%" + s + "%"))
        query.From |> Option.iter (fun f ->
            conditions.Append(" AND Date >= @From") |> ignore
            parameters.Add("From", f))
        query.To |> Option.iter (fun t ->
            conditions.Append(" AND Date < @To") |> ignore
            parameters.Add("To", t))
        let whereClause = conditions.ToString()
        let total = conn.QuerySingle<int>(sprintf "SELECT COUNT(*) FROM Expenses %s" whereClause, parameters)
        parameters.Add("Limit", query.PageSize)
        parameters.Add("Offset", (query.Page - 1) * query.PageSize)
        let items =
            conn.Query<Expense>(
                sprintf "SELECT * FROM Expenses %s ORDER BY Date DESC, Id DESC LIMIT @Limit OFFSET @Offset" whereClause,
                parameters)
            |> List.ofSeq
        { Items = items; Total = total; Page = query.Page; PageSize = query.PageSize }

    let updateExpense userId (id: int) (expense: ExpenseDto) =
        use conn = new SqliteConnection(connectionString)
        let sql = """
            UPDATE Expenses
            SET Amount = @Amount, Currency = @Currency, Category = @Category,
                Merchant = @Merchant, Description = @Description, Date = @Date
            WHERE Id = @Id AND UserId = @UserId
        """
        conn.Execute(sql, {| Id = id; UserId = userId; Amount = expense.Amount; Currency = expense.Currency; Category = expense.Category; Merchant = expense.Merchant; Description = expense.Description; Date = expense.Date |}) > 0

    /// Deletes an expense together with its anomalies in one transaction.
    let deleteExpense userId (id: int) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()
        use tx = conn.BeginTransaction()
        conn.Execute("DELETE FROM Anomalies WHERE UserId = @UserId AND ExpenseId = @Id", {| UserId = userId; Id = id |}, tx) |> ignore
        let affected = conn.Execute("DELETE FROM Expenses WHERE UserId = @UserId AND Id = @Id", {| UserId = userId; Id = id |}, tx)
        tx.Commit()
        affected > 0

    let private insertAnomalySql = """
        INSERT INTO Anomalies (UserId, ExpenseId, RuleCode, Score, Severity, Reason, Recommendation, DetectedAt, IsResolved)
        VALUES (@UserId, @ExpenseId, @RuleCode, @Score, @Severity, @Reason, @Recommendation, @DetectedAt, @IsResolved);
    """

    [<CLIMutable>]
    type private AnomalyKey = { ExpenseId : int; RuleCode : string }

    /// (ExpenseId, RuleCode) pairs the user has already marked as resolved.
    let getResolvedAnomalyKeys userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<AnomalyKey>("SELECT ExpenseId, RuleCode FROM Anomalies WHERE UserId = @UserId AND IsResolved = 1", {| UserId = userId |})
        |> Seq.map (fun k -> k.ExpenseId, k.RuleCode)
        |> List.ofSeq

    /// Atomically swaps the user's unresolved anomalies for a fresh detection run.
    let replaceUnresolvedAnomalies userId (anomalies: Anomaly list) =
        use conn = new SqliteConnection(connectionString)
        conn.Open()
        use tx = conn.BeginTransaction()
        conn.Execute("DELETE FROM Anomalies WHERE UserId = @UserId AND IsResolved = 0", {| UserId = userId |}, tx) |> ignore
        for anomaly in anomalies do
            conn.Execute(insertAnomalySql, {| anomaly with UserId = userId |}, tx) |> ignore
        tx.Commit()

    let resolveAnomaly userId (id: int) =
        use conn = new SqliteConnection(connectionString)
        let sql = "UPDATE Anomalies SET IsResolved = 1 WHERE Id = @Id AND UserId = @UserId"
        conn.Execute(sql, {| Id = id; UserId = userId |}) > 0

    let getAnomalies userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Anomaly>("SELECT * FROM Anomalies WHERE UserId = @UserId AND IsResolved = 0 ORDER BY DetectedAt DESC LIMIT 100", {| UserId = userId |}) |> List.ofSeq

    let setBudget userId (category: string) (limit: decimal) =
        use conn = new SqliteConnection(connectionString)
        let sql = "INSERT OR REPLACE INTO Budgets (UserId, Category, LimitAmount) VALUES (@UserId, @Category, @LimitAmount)"
        conn.Execute(sql, {| UserId = userId; Category = category; LimitAmount = limit |}) |> ignore

    let deleteBudget userId (category: string) =
        use conn = new SqliteConnection(connectionString)
        conn.Execute("DELETE FROM Budgets WHERE UserId = @UserId AND Category = @Category", {| UserId = userId; Category = category |}) > 0

    let getBudgets userId =
        use conn = new SqliteConnection(connectionString)
        conn.Query<Budget>("SELECT * FROM Budgets WHERE UserId = @UserId", {| UserId = userId |}) |> List.ofSeq

    let getUserByUsername (username: string) =
        use conn = new SqliteConnection(connectionString)
        conn.Query<User>("SELECT * FROM Users WHERE Username = @Username", {| Username = username |}) |> Seq.tryHead

    let getUserById (id: int) =
        use conn = new SqliteConnection(connectionString)
        conn.Query<User>("SELECT * FROM Users WHERE Id = @Id", {| Id = id |}) |> Seq.tryHead

    let updatePassword userId (passwordHash: string) =
        use conn = new SqliteConnection(connectionString)
        conn.Execute("UPDATE Users SET PasswordHash = @PasswordHash WHERE Id = @Id", {| Id = userId; PasswordHash = passwordHash |}) > 0

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
