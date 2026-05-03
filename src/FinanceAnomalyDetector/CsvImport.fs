namespace FinanceAnomalyDetector

open System
open System.IO
open CsvHelper
open CsvHelper.Configuration
open System.Globalization

module CsvImport =
    type CsvExpense = {
        Date: string
        Amount: string
        Currency: string
        Category: string
        Merchant: string
        Description: string
    }
    
    let importCsv (reader: TextReader) =
        let config = CsvConfiguration(CultureInfo.InvariantCulture, HasHeaderRecord = true, MissingFieldFound = null, PrepareHeaderForMatch = PrepareHeaderForMatch(fun args -> args.Header.ToLower()))
        use csv = new CsvReader(reader, config)
        let records = csv.GetRecords<CsvExpense>() |> List.ofSeq
        
        let mutable imported = 0
        let mutable skipped = 0
        let mutable errors = []
        
        for record in records do
            let parsedAmount = match Decimal.TryParse(record.Amount, NumberStyles.Any, CultureInfo.InvariantCulture) with | true, v -> Some v | _ -> None
            let parsedDate = match DateTime.TryParse(record.Date) with | true, v -> Some v | _ -> None
            
            match parsedAmount, parsedDate with
            | Some amt, Some date ->
                let dto : ExpenseDto = {
                    Amount = amt
                    Currency = if String.IsNullOrWhiteSpace record.Currency then "USD" else record.Currency
                    Category = if String.IsNullOrWhiteSpace record.Category then "Unknown" else record.Category
                    Merchant = if String.IsNullOrWhiteSpace record.Merchant then "Unknown" else record.Merchant
                    Description = if String.IsNullOrWhiteSpace record.Description then "" else record.Description
                    Date = date
                }
                match Validation.validateExpense dto with
                | Ok validDto ->
                    Storage.insertExpense validDto |> ignore
                    imported <- imported + 1
                | Error errs ->
                    skipped <- skipped + 1
                    errors <- errs @ errors
            | _ ->
                skipped <- skipped + 1
                errors <- "Invalid date or amount format" :: errors
                
        { ImportedRows = imported; SkippedRows = skipped; ValidationErrors = errors |> List.distinct }
