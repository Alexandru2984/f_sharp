namespace FinanceAnomalyDetector

open System

/// Deterministic sample dataset for empty accounts: ~5 months of realistic
/// spending that exercises every detection rule (recurring subscriptions
/// with a price hike, a duplicate charge, a night purchase, an outlier)
/// plus a second currency so the currency selector has something to show.
module DemoData =

    let budgets = [
        "Groceries", 400m
        "Food", 250m
        "Entertainment", 60m
    ]

    let private dto amount currency category merchant description (date: DateTime) : ExpenseDto =
        { Amount = amount
          Currency = currency
          Category = category
          Merchant = merchant
          Description = description
          Date = date }

    /// Generates the dataset relative to `now`; `seed` varies the jitter so
    /// two demo accounts don't look byte-identical.
    let generate (seed: int) (now: DateTime) =
        let rng = Random(seed)
        let jitter (base_: decimal) (spread: decimal) =
            base_ + decimal (rng.NextDouble()) * spread * 2m - spread

        // Anchor months oldest-first: 4 months back through the current one.
        let months = [ for back in 4 .. -1 .. 0 -> now.AddMonths(-back) ]
        let day (anchor: DateTime) d hour minute =
            DateTime(anchor.Year, anchor.Month, min d (DateTime.DaysInMonth(anchor.Year, anchor.Month)), hour, minute, 0)

        let recurring =
            months |> List.indexed |> List.collect (fun (i, m) ->
                let isCurrent = i = months.Length - 1
                [ dto 1200m "USD" "Housing" "Sunrise Apartments" "Monthly rent" (day m 1 9 0)
                  // Netflix price hike lands in the current month.
                  dto (if isCurrent then 19.99m else 15.99m) "USD" "Entertainment" "Netflix" "Subscription" (day m 5 12 0)
                  dto 9.99m "USD" "Entertainment" "Spotify" "Subscription" (day m 7 12 30)
                  dto 45m "USD" "Health" "PulseGym" "Membership" (day m 3 18 0)
                  dto (Math.Round(jitter 82m 8m, 2)) "USD" "Utilities" "PowerGrid Co" "Electricity" (day m 15 10 0) ])

        let groceries =
            months |> List.collect (fun m ->
                [ for d in [2; 9; 16; 23] ->
                    let merchant = if d % 8 = 0 then "Carrefour" else "Lidl"
                    dto (Math.Round(jitter 62m 22m, 2)) "USD" "Groceries" merchant "Weekly shop" (day m d 17 30) ])

        let food =
            months |> List.collect (fun m ->
                [ for d in [6; 13; 27] ->
                    let merchant = [| "La Piazza"; "Sushi Go"; "Burger Hub" |].[rng.Next(3)]
                    dto (Math.Round(jitter 38m 16m, 2)) "USD" "Food" merchant "Eating out" (day m d 19 45) ])

        let transport =
            months |> List.collect (fun m ->
                [ for d in [4; 11; 18; 25] ->
                    let merchant = if d % 2 = 0 then "Metro Card" else "Uber"
                    dto (Math.Round(jitter 11m 5m, 2)) "USD" "Transport" merchant "" (day m d 8 15) ])

        let travel =
            let m = months.[1]
            [ dto 120m "EUR" "Travel" "AirConnect" "Flight" (day m 10 6 40)
              dto 200m "EUR" "Travel" "Hotel Aurora" "2 nights" (day m 11 14 0)
              dto 25m "EUR" "Travel" "City Museum" "Tickets" (day m 12 11 0) ]

        let current = List.last months
        let incidents =
            [ // duplicate charge, 4 minutes apart
              dto 249.99m "USD" "Shopping" "TechWorld" "Headphones" (day current 12 16 20)
              dto 249.99m "USD" "Shopping" "TechWorld" "Headphones" (day current 12 16 24)
              // deep-night purchase
              dto 89.5m "USD" "Other" "CityBar" "" (day current 13 2 37)
              // grocery outlier vs the usual 40-85 range
              dto 720m "USD" "Groceries" "Lidl" "Party stock-up" (day current 20 18 0) ]

        recurring @ groceries @ food @ transport @ travel @ incidents
