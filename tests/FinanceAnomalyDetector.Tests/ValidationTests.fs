module ValidationTests

open System
open Xunit
open FinanceAnomalyDetector
open TestHelpers

[<Fact>]
let ``valid amounts are accepted`` () =
    Assert.True(Validation.isValidAmount 0.01m)
    Assert.True(Validation.isValidAmount 99999999m)

[<Fact>]
let ``zero, negative and absurd amounts are rejected`` () =
    Assert.False(Validation.isValidAmount 0m)
    Assert.False(Validation.isValidAmount -5m)
    Assert.False(Validation.isValidAmount 100000000m)

[<Fact>]
let ``a well-formed expense passes validation`` () =
    let dto = mkDto 12.5m "Food" "KFC" DateTime.UtcNow
    match Validation.validateExpense dto with
    | Ok _ -> ()
    | Error errs -> failwithf "expected Ok, got %A" errs

[<Fact>]
let ``blank currency, category and merchant are each reported`` () =
    let dto = { mkDto 12.5m "" "" DateTime.UtcNow with Currency = " " }
    match Validation.validateExpense dto with
    | Ok _ -> failwith "expected Error"
    | Error errs -> Assert.Equal(3, errs.Length)

[<Fact>]
let ``overlong fields are rejected`` () =
    let dto = mkDto 10m (String.replicate 101 "a") "M" DateTime.UtcNow
    match Validation.validateExpense dto with
    | Ok _ -> failwith "expected Error"
    | Error errs -> Assert.Contains(errs, fun (e: string) -> e.Contains "Category")

[<Fact>]
let ``password policy enforces length bounds`` () =
    match Validation.validatePassword "short" with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error for short password"
    match Validation.validatePassword (String.replicate 129 "x") with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error for overlong password"
    match Validation.validatePassword "long-enough-password" with
    | Ok () -> ()
    | Error e -> failwithf "expected Ok, got %A" e

[<Fact>]
let ``usernames must be 3-50 chars from the safe alphabet`` () =
    match Validation.validateRegistration "ab" "validpassword" with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error for short username"
    match Validation.validateRegistration "has spaces" "validpassword" with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error for spaces"
    match Validation.validateRegistration "<script>" "validpassword" with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error for symbols"
    match Validation.validateRegistration "good.user-name_1" "validpassword" with
    | Ok () -> ()
    | Error e -> failwithf "expected Ok, got %A" e
