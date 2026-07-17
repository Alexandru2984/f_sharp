namespace FinanceAnomalyDetector

open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

module AnomalyEngine =
    open AnomalyRules

    /// One pass builds per-category/per-merchant aggregates; each expense is
    /// then scored against O(1) leave-one-out views of its own groups, and
    /// SUB_HIKE runs as a chronological walk per merchant. Keeps a full run
    /// near-linear instead of O(n^2).
    let detectAll (expenses: Expense list) =
        let byCategory = expenses |> List.groupBy (fun e -> e.Category) |> Map.ofList
        let byMerchant = expenses |> List.groupBy (fun e -> e.Merchant) |> Map.ofList
        let categoryAggs = byCategory |> Map.map (fun _ group -> AmountAggregate.ofExpenses group)
        let merchantAggs = byMerchant |> Map.map (fun _ group -> AmountAggregate.ofExpenses group)

        let perExpense =
            expenses
            |> List.collect (fun e ->
                let merchantGroup = byMerchant.[e.Merchant]
                [ checkCategoryOutlierAgg e (AmountAggregate.excluding e.Amount categoryAggs.[e.Category])
                  checkMerchantSpikeAgg e (AmountAggregate.excluding e.Amount merchantAggs.[e.Merchant])
                  checkCategoryChange e merchantGroup
                  checkDuplicate e merchantGroup
                  checkNightSpending e ]
                |> List.choose id)

        let subscriptionHikes =
            byMerchant |> Map.toList |> List.collect (snd >> detectSubscriptionHikes)

        perExpense @ subscriptionHikes

    /// Re-evaluates every rule for the user's full history. Findings the user
    /// already resolved stay resolved: a fresh anomaly matching a resolved
    /// (ExpenseId, RuleCode) pair - or a pre-RuleCode 'LEGACY' resolution for
    /// the same expense - is suppressed instead of resurrected.
    let runAll userId =
        let expenses = Storage.getAllExpenses userId
        let resolvedKeys = Storage.getResolvedAnomalyKeys userId |> Set.ofList
        let isSuppressed (a: Anomaly) =
            Set.contains (a.ExpenseId, a.RuleCode) resolvedKeys
            || Set.contains (a.ExpenseId, "LEGACY") resolvedKeys
        let fresh =
            detectAll expenses
            |> List.filter (fun a -> not (isSuppressed a))
        Storage.replaceUnresolvedAnomalies userId fresh
        fresh.Length

    // ---- background execution ----

    let private userLocks = ConcurrentDictionary<int, SemaphoreSlim>()

    /// Fire-and-forget detection run, serialized per user so concurrent data
    /// changes never interleave two runs. Failures are logged, not thrown.
    let triggerBackgroundRun (log: exn -> unit) userId =
        let semaphore = userLocks.GetOrAdd(userId, fun _ -> new SemaphoreSlim(1, 1))
        Task.Run(fun () ->
            semaphore.Wait()
            try
                try runAll userId |> ignore
                with ex -> log ex
            finally
                semaphore.Release() |> ignore)
        |> ignore
