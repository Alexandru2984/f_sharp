module CsvImportTests

open System.IO
open Xunit
open FinanceAnomalyDetector

let private parse (csv: string) =
    use reader = new StringReader(csv)
    CsvImport.parseCsv reader

[<Fact>]
let ``well-formed rows are parsed`` () =
    let result = parse """Date,Amount,Currency,Category,Merchant,Description
2026-05-03,100.50,USD,Groceries,Walmart,Weekly
2026-05-04,20,EUR,Food,KFC,Lunch"""
    Assert.Equal(2, result.Valid.Length)
    Assert.Equal(0, result.Skipped)
    Assert.Empty(result.Errors)

[<Fact>]
let ``headers are matched case-insensitively`` () =
    let result = parse """date,amount,currency,category,merchant,description
2026-05-03,10,USD,Food,KFC,x"""
    Assert.Equal(1, result.Valid.Length)

[<Fact>]
let ``bad rows are skipped with row-numbered errors`` () =
    let result = parse """Date,Amount,Currency,Category,Merchant,Description
not-a-date,100,USD,Food,KFC,x
2026-05-03,abc,USD,Food,KFC,x
2026-05-04,10,USD,Food,KFC,ok"""
    Assert.Equal(1, result.Valid.Length)
    Assert.Equal(2, result.Skipped)
    Assert.Contains(result.Errors, fun e -> e.StartsWith "Row 2")
    Assert.Contains(result.Errors, fun e -> e.StartsWith "Row 3")

[<Fact>]
let ``missing optional fields get defaults`` () =
    let result = parse """Date,Amount,Currency,Category,Merchant,Description
2026-05-03,10,,,,"""
    let dto = Assert.Single(result.Valid)
    Assert.Equal("USD", dto.Currency)
    Assert.Equal("Unknown", dto.Category)
    Assert.Equal("Unknown", dto.Merchant)

[<Fact>]
let ``negative amounts fail validation and are skipped`` () =
    let result = parse """Date,Amount,Currency,Category,Merchant,Description
2026-05-03,-10,USD,Food,KFC,x"""
    Assert.Equal(0, result.Valid.Length)
    Assert.Equal(1, result.Skipped)
