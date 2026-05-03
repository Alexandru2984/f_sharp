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

    let loginHandler : HttpHandler =
        fun next ctx ->
            task {
                let! dto = ctx.BindJsonAsync<LoginRequest>()
                match Storage.getUserByUsername dto.Username with
                | Some user when BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash) ->
                    let claims = [| Claim(ClaimTypes.Name, user.Username) |]
                    let identity = ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)
                    let principal = ClaimsPrincipal(identity)
                    do! ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal)
                    return! json {| status = "success" |} next ctx
                | _ ->
                    return! (setStatusCode 401 >=> json {| error = "Invalid credentials" |}) next ctx
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
        fun next ctx -> json (Storage.getExpenses()) next ctx
        
    let postExpenseHandler : HttpHandler =
        fun next ctx ->
            task {
                let! dto = ctx.BindJsonAsync<ExpenseDto>()
                match Validation.validateExpense dto with
                | Ok validDto -> 
                    let exp = Storage.insertExpense validDto
                    return! json exp next ctx
                | Error errors ->
                    return! (setStatusCode 400 >=> json {| errors = errors |}) next ctx
            }
            
    let importCsvHandler : HttpHandler =
        fun next ctx ->
            task {
                if not ctx.Request.HasFormContentType then
                    return! (setStatusCode 400 >=> json {| error = "Form content required" |}) next ctx
                else
                    let! form = ctx.Request.ReadFormAsync()
                    let file = form.Files.GetFile("file")
                    if isNull file then
                        return! (setStatusCode 400 >=> json {| error = "No file uploaded" |}) next ctx
                    else
                        use stream = file.OpenReadStream()
                        use reader = new StreamReader(stream)
                        let result = CsvImport.importCsv reader
                        return! json result next ctx
            }

    let getAnomaliesHandler : HttpHandler =
        fun next ctx -> json (Storage.getAnomalies()) next ctx
        
    let runAnomaliesHandler : HttpHandler =
        fun next ctx ->
            let count = AnomalyEngine.runAll()
            json {| detected = count; message = sprintf "Anomaly detection completed. Found %d anomalies." count |} next ctx
            
    let patchAnomalyHandler (id: int) : HttpHandler =
        fun next ctx ->
            Storage.resolveAnomaly id
            json {| status = "success" |} next ctx
            
    let getStatsHandler : HttpHandler =
        fun next ctx -> json (Stats.getDashboardStats()) next ctx
        
    let getCategoriesHandler : HttpHandler =
        fun next ctx -> json (Stats.getCategoryBreakdown()) next ctx

    let getTrendsHandler : HttpHandler =
        fun next ctx -> json (Stats.getMonthlyTrends()) next ctx

    let getBudgetsHandler : HttpHandler =
        fun next ctx -> json (Stats.getBudgetStatus()) next ctx

    let postBudgetHandler : HttpHandler =
        fun next ctx ->
            task {
                let! dto = ctx.BindJsonAsync<Budget>()
                Storage.setBudget dto.Category dto.LimitAmount
                return! json {| status = "success" |} next ctx
            }

    let apiRoutes =
        requiresAuthentication (challenge CookieAuthenticationDefaults.AuthenticationScheme) >=> choose [
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
            POST >=> route "/api/logout" >=> logoutHandler
            apiRoutes
        ]

    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        
        DotNetEnv.Env.Load("/home/micu/f_sharp/.env")
        let port = Environment.GetEnvironmentVariable("APP_PORT")
        let portStr = if String.IsNullOrEmpty(port) then "5000" else port
        
        builder.WebHost.ConfigureKestrel(fun serverOptions -> 
            serverOptions.ListenLocalhost(int portStr)
        ) |> ignore

        builder.Services.AddGiraffe() |> ignore
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
               .AddCookie(fun options -> 
                   options.Events.OnRedirectToLogin <- fun context -> 
                       context.Response.StatusCode <- 401
                       System.Threading.Tasks.Task.CompletedTask
               ) |> ignore
        builder.Services.AddAuthorization() |> ignore
        
        let app = builder.Build()
        
        Storage.initDb()
        Storage.seedAdmin()

        app.UseAuthentication() |> ignore
        app.UseAuthorization() |> ignore

        // Serve files from public folder
        let staticFileOptions = Microsoft.AspNetCore.Builder.StaticFileOptions()
        staticFileOptions.FileProvider <- new Microsoft.Extensions.FileProviders.PhysicalFileProvider("/home/micu/f_sharp/public")
        app.UseStaticFiles(staticFileOptions) |> ignore

        // Add default document logic (redirect / to /index.html implicitly by UseDefaultFiles or explicit middleware)
        app.Use(fun (context: HttpContext) (next: RequestDelegate) -> 
            task {
                if context.Request.Path.Value = "/" then
                    context.Request.Path <- PathString("/index.html")
                if context.Request.Path.Value = "/docs" then
                    context.Request.Path <- PathString("/docs.html")
                return! next.Invoke(context)
            } :> System.Threading.Tasks.Task
        ) |> ignore

        app.UseStaticFiles(staticFileOptions) |> ignore

        app.UseGiraffeErrorHandler(errorHandler) |> ignore
        app.UseGiraffe(webApp)
        
        app.Run()
        0
