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

    type ParseResult = {
        Valid : ExpenseDto list
        Skipped : int
        Errors : string list
    }

    [<Literal>]
    let MaxRows = 10000

    /// Pure CSV parsing + validation; persistence is the caller's concern.
    let parseCsv (reader: TextReader) =
        let config = CsvConfiguration(CultureInfo.InvariantCulture, HasHeaderRecord = true, MissingFieldFound = null, PrepareHeaderForMatch = PrepareHeaderForMatch(fun args -> args.Header.ToLower()))
        use csv = new CsvReader(reader, config)
        let records = csv.GetRecords<CsvExpense>() |> Seq.truncate (MaxRows + 1) |> List.ofSeq

        if records.Length > MaxRows then
            { Valid = []; Skipped = 0; Errors = [ sprintf "CSV exceeds the maximum of %d rows." MaxRows ] }
        else
            let mutable valid = []
            let mutable skipped = 0
            let mutable errors = []

            for i, record in List.indexed records do
                let rowNo = i + 2 // 1-based, plus header row
                let parsedAmount = match Decimal.TryParse(record.Amount, NumberStyles.Any, CultureInfo.InvariantCulture) with | true, v -> Some v | _ -> None
                let parsedDate = match DateTime.TryParse(record.Date, CultureInfo.InvariantCulture, DateTimeStyles.None) with | true, v -> Some v | _ -> None

                match parsedAmount, parsedDate with
                | Some amt, Some date ->
                    let dto : ExpenseDto = {
                        Amount = amt
                        Currency = if String.IsNullOrWhiteSpace record.Currency then "USD" else record.Currency.Trim()
                        Category = if String.IsNullOrWhiteSpace record.Category then "Unknown" else record.Category.Trim()
                        Merchant = if String.IsNullOrWhiteSpace record.Merchant then "Unknown" else record.Merchant.Trim()
                        Description = if String.IsNullOrWhiteSpace record.Description then "" else record.Description.Trim()
                        Date = date
                    }
                    match Validation.validateExpense dto with
                    | Ok validDto -> valid <- validDto :: valid
                    | Error errs ->
                        skipped <- skipped + 1
                        errors <- (errs |> List.map (sprintf "Row %d: %s" rowNo)) @ errors
                | _ ->
                    skipped <- skipped + 1
                    errors <- sprintf "Row %d: invalid date or amount format" rowNo :: errors

            { Valid = List.rev valid; Skipped = skipped; Errors = List.rev errors |> List.distinct }
