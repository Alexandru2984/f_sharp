namespace FinanceAnomalyDetector

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Giraffe
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open System.Security.Claims

module Program =
    let errorHandler (ex: Exception) (logger: ILogger) =
        logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
        clearResponse >=> setStatusCode 500 >=> json {| error = "Internal server error" |}

    let authChallenge : HttpHandler =
        setStatusCode 401 >=> json {| error = "Unauthorized" |}

    let getUserId (ctx: HttpContext) =
        let claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier)
        if isNull claim then 0 else int claim.Value

    /// Binds the request body as JSON, mapping malformed payloads to a 400
    /// instead of letting the exception surface as a 500.
    let tryBindJson<'T> (ctx: HttpContext) =
        task {
            try
                let! dto = ctx.BindJsonAsync<'T>()
                return Ok dto
            with _ ->
                return Error "Invalid JSON body"
        }

    let badRequest (message: string) : HttpHandler =
        setStatusCode 400 >=> json {| error = message |}

    let signInUser (ctx: HttpContext) (user: User) =
        let claims = [|
            Claim(ClaimTypes.Name, user.Username)
            Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        |]
        let identity = ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
        ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ClaimsPrincipal(identity))

    let loginHandler : HttpHandler =
        fun next ctx ->
            task {
                match! tryBindJson<LoginRequest> ctx with
                | Error msg -> return! badRequest msg next ctx
                | Ok dto ->
                    match Storage.getUserByUsername dto.Username with
                    | Some user when BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash) ->
                        do! signInUser ctx user
                        return! json {| status = "success" |} next ctx
                    | _ ->
                        return! (setStatusCode 401 >=> json {| error = "Invalid credentials" |}) next ctx
            }

    let registerHandler : HttpHandler =
        fun next ctx ->
            task {
                match! tryBindJson<LoginRequest> ctx with
                | Error msg -> return! badRequest msg next ctx
                | Ok dto ->
                    match Validation.validateRegistration dto.Username dto.Password with
                    | Error errors ->
                        return! (setStatusCode 400 >=> json {| errors = errors |}) next ctx
                    | Ok () ->
                        match Storage.getUserByUsername dto.Username with
                        | Some _ ->
                            return! badRequest "Username already exists" next ctx
                        | None ->
                            try
                                let hash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
                                let user = Storage.createUser dto.Username hash
                                do! signInUser ctx user
                                return! json {| status = "success" |} next ctx
                            with :? Microsoft.Data.Sqlite.SqliteException ->
                                // Unique constraint race between the check and the insert.
                                return! badRequest "Username already exists" next ctx
            }

    let logoutHandler : HttpHandler =
        fun next ctx ->
            task {
                do! ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)
                return! json {| status = "success" |} next ctx
            }

    let getHealth : HttpHandler =
        fun next ctx -> json {| status = "OK" |} next ctx
        
    let getExpensesHandler : HttpHandler =
        fun next ctx -> json (Storage.getExpenses (getUserId ctx)) next ctx
        
    let postExpenseHandler : HttpHandler =
        fun next ctx ->
            task {
                match! tryBindJson<ExpenseDto> ctx with
                | Error msg -> return! badRequest msg next ctx
                | Ok dto ->
                    match Validation.validateExpense dto with
                    | Ok validDto ->
                        let exp = Storage.insertExpense (getUserId ctx) validDto
                        return! json exp next ctx
                    | Error errors ->
                        return! (setStatusCode 400 >=> json {| errors = errors |}) next ctx
            }
            
    [<Literal>]
    let MaxCsvUploadBytes = 5L * 1024L * 1024L

    let importCsvHandler : HttpHandler =
        fun next ctx ->
            task {
                if not ctx.Request.HasFormContentType then
                    return! badRequest "Form content required" next ctx
                else
                    let! form = ctx.Request.ReadFormAsync()
                    let file = form.Files.GetFile("file")
                    if isNull file then
                        return! badRequest "No file uploaded" next ctx
                    elif file.Length > MaxCsvUploadBytes then
                        return! badRequest "File exceeds the 5 MB upload limit" next ctx
                    else
                        use stream = file.OpenReadStream()
                        use reader = new StreamReader(stream)
                        try
                            let parsed = CsvImport.parseCsv reader
                            let imported = Storage.insertExpenses (getUserId ctx) parsed.Valid
                            let result = { ImportedRows = imported; SkippedRows = parsed.Skipped; ValidationErrors = parsed.Errors }
                            return! json result next ctx
                        with :? CsvHelper.CsvHelperException ->
                            return! badRequest "Could not parse the CSV file; check the header row and delimiters" next ctx
            }

    let getAnomaliesHandler : HttpHandler =
        fun next ctx -> json (Storage.getAnomalies (getUserId ctx)) next ctx
        
    let runAnomaliesHandler : HttpHandler =
        fun next ctx ->
            let count = AnomalyEngine.runAll (getUserId ctx)
            json {| detected = count; message = sprintf "Anomaly detection completed. Found %d anomalies." count |} next ctx
            
    let patchAnomalyHandler (id: int) : HttpHandler =
        fun next ctx ->
            Storage.resolveAnomaly (getUserId ctx) id
            json {| status = "success" |} next ctx
            
    let getStatsHandler : HttpHandler =
        fun next ctx -> json (Stats.getDashboardStats (getUserId ctx)) next ctx
        
    let getCategoriesHandler : HttpHandler =
        fun next ctx -> json (Stats.getCategoryBreakdown (getUserId ctx)) next ctx

    let getTrendsHandler : HttpHandler =
        fun next ctx -> json (Stats.getMonthlyTrends (getUserId ctx)) next ctx

    let getBudgetsHandler : HttpHandler =
        fun next ctx -> json (Stats.getBudgetStatus (getUserId ctx)) next ctx

    let postBudgetHandler : HttpHandler =
        fun next ctx ->
            task {
                match! tryBindJson<Budget> ctx with
                | Error msg -> return! badRequest msg next ctx
                | Ok dto ->
                    if not (Validation.isValidString 100 dto.Category) then
                        return! badRequest "Category is required and max 100 chars." next ctx
                    elif dto.LimitAmount <= 0m then
                        return! badRequest "Limit must be greater than 0." next ctx
                    else
                        Storage.setBudget (getUserId ctx) dto.Category dto.LimitAmount
                        return! json {| status = "success" |} next ctx
            }

    let apiRoutes =
        requiresAuthentication authChallenge >=> choose [
            GET >=> choose [
                route "/api/expenses" >=> getExpensesHandler
                route "/api/anomalies" >=> getAnomaliesHandler
                route "/api/stats" >=> getStatsHandler
                route "/api/categories" >=> getCategoriesHandler
                route "/api/trends" >=> getTrendsHandler
                route "/api/budgets" >=> getBudgetsHandler
                route "/api/me" >=> fun next ctx -> json {| username = ctx.User.Identity.Name |} next ctx
            ]
            POST >=> choose [
                route "/api/expenses" >=> postExpenseHandler
                route "/api/expenses/import-csv" >=> importCsvHandler
                route "/api/anomalies/run" >=> runAnomaliesHandler
                route "/api/budgets" >=> postBudgetHandler
            ]
            PATCH >=> choose [
                routef "/api/anomalies/%i/resolve" patchAnomalyHandler
            ]
        ]

    let webApp =
        choose [
            GET >=> route "/health" >=> getHealth
            POST >=> route "/api/login" >=> loginHandler
            POST >=> route "/api/register" >=> registerHandler
            POST >=> route "/api/logout" >=> logoutHandler
            apiRoutes
        ]

    /// Reads an environment variable with a fallback default.
    let envOr (name: string) (fallback: string) =
        let value = Environment.GetEnvironmentVariable(name)
        if String.IsNullOrWhiteSpace(value) then fallback else value

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)

        // Loads the nearest .env walking up from the working directory (repo
        // root in dev, publish dir in prod via systemd EnvironmentFile anyway).
        // NoClobber: real environment variables always win over .env entries.
        DotNetEnv.Env.TraversePath().NoClobber().Load() |> ignore

        let port =
            match Int32.TryParse(envOr "APP_PORT" "5000") with
            | true, p -> p
            | _ -> 5000

        let publicDir =
            let configured = envOr "PUBLIC_DIR" (Path.Combine(Directory.GetCurrentDirectory(), "public"))
            if Directory.Exists(configured) then Path.GetFullPath(configured)
            else Path.Combine(AppContext.BaseDirectory, "public")

        Storage.configure (envOr "DB_PATH" (Path.Combine("data", "finance.db")))

        // ASPNETCORE_URLS (e.g. in containers) takes precedence; otherwise
        // bind to localhost only, since nginx fronts the app.
        if String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) then
            builder.WebHost.ConfigureKestrel(fun serverOptions ->
                serverOptions.ListenLocalhost(port)
            ) |> ignore

        builder.Services.AddGiraffe() |> ignore

        // Brute-force protection: strict per-IP fixed window on auth endpoints.
        builder.Services.AddRateLimiter(fun options ->
            options.RejectionStatusCode <- StatusCodes.Status429TooManyRequests
            options.GlobalLimiter <-
                System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(fun ctx ->
                    let path = ctx.Request.Path.Value
                    let isAuthEndpoint = path = "/api/login" || path = "/api/register"
                    if isAuthEndpoint then
                        let ip =
                            match ctx.Connection.RemoteIpAddress with
                            | null -> "unknown"
                            | addr -> addr.ToString()
                        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                            "auth:" + ip,
                            fun _ ->
                                System.Threading.RateLimiting.FixedWindowRateLimiterOptions(
                                    PermitLimit = 10,
                                    Window = TimeSpan.FromMinutes(1.0),
                                    QueueLimit = 0
                                ))
                    else
                        System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("global"))
        ) |> ignore

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
               .AddCookie(fun options -> 
                   options.Events.OnRedirectToLogin <- fun context -> 
                       context.Response.StatusCode <- 401
                       System.Threading.Tasks.Task.CompletedTask
                   options.Cookie.HttpOnly <- true
                   options.Cookie.SecurePolicy <- CookieSecurePolicy.Always
                   options.Cookie.SameSite <- Microsoft.AspNetCore.Http.SameSiteMode.Strict
               ) |> ignore
        builder.Services.AddAuthorization() |> ignore
        
        let app = builder.Build()
        
        Storage.initDb()
        Storage.seedAdmin()

        // Behind nginx: trust loopback proxy for the real client IP and scheme,
        // so rate limiting and Secure cookies behave correctly.
        let forwardedOptions = ForwardedHeadersOptions()
        forwardedOptions.ForwardedHeaders <-
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            ||| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
        app.UseForwardedHeaders(forwardedOptions) |> ignore

        app.UseRateLimiter() |> ignore
        app.UseAuthentication() |> ignore
        app.UseAuthorization() |> ignore

        // Map friendly routes to static documents before the static file middleware runs.
        app.Use(fun (context: HttpContext) (next: RequestDelegate) ->
            task {
                if context.Request.Path.Value = "/" then
                    context.Request.Path <- PathString("/index.html")
                if context.Request.Path.Value = "/docs" then
                    context.Request.Path <- PathString("/docs.html")
                return! next.Invoke(context)
            } :> System.Threading.Tasks.Task
        ) |> ignore

        let staticFileOptions = Microsoft.AspNetCore.Builder.StaticFileOptions()
        staticFileOptions.FileProvider <- new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicDir)
        app.UseStaticFiles(staticFileOptions) |> ignore

        app.UseGiraffeErrorHandler(errorHandler) |> ignore
        app.UseGiraffe(webApp)
        
        app.Run()
        0