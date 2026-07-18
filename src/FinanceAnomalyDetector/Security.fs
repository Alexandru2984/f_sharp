namespace FinanceAnomalyDetector

open System
open Microsoft.AspNetCore.Http

/// Cross-cutting security helpers: real client IP behind Cloudflare/nginx,
/// and same-origin validation for CSRF defense-in-depth.
module Security =

    let private hostOfUrl (url: string) =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, u -> Some (u.Host.ToLowerInvariant())
        | _ -> None

    /// Whether a state-changing request's Origin (or Referer fallback) targets
    /// an allowed host. A mutating request with neither header is rejected:
    /// browsers always send at least one on same-origin POST/PUT/DELETE/PATCH,
    /// so absence signals a forged or non-browser cross-origin call.
    let originAllowed (allowedHosts: Set<string>) (requestHost: string) (origin: string option) (referer: string option) =
        // When an explicit allowlist is configured (production) trust only it,
        // ignoring the request's own Host header (which a client can influence).
        // Otherwise (dev) fall back to matching the request host.
        let permitted h =
            if Set.isEmpty allowedHosts then h = requestHost.ToLowerInvariant()
            else Set.contains h allowedHosts
        match origin |> Option.bind hostOfUrl with
        | Some h -> permitted h
        | None ->
            match referer |> Option.bind hostOfUrl with
            | Some h -> permitted h
            | None -> false

    let private headerValue (ctx: HttpContext) (name: string) =
        match ctx.Request.Headers.TryGetValue(name) with
        | true, v when v.Count > 0 && not (String.IsNullOrWhiteSpace v[0]) -> Some (v[0].Trim())
        | _ -> None

    /// Real client IP for rate limiting. This trusts only RemoteIpAddress,
    /// which the ForwardedHeaders middleware derives from the proxy chain.
    /// nginx restores the true client from CF-Connecting-IP *only* for
    /// connections originating in Cloudflare's ranges (set_real_ip_from), so a
    /// forged CF-Connecting-IP on a direct-to-origin request cannot spoof or
    /// poison a rate-limit bucket.
    let clientIp (ctx: HttpContext) =
        match ctx.Connection.RemoteIpAddress with
        | null -> "unknown"
        | addr -> addr.ToString()

    let private mutatingMethods = set [ "POST"; "PUT"; "DELETE"; "PATCH" ]

    /// True when the request changes state on an /api route and must therefore
    /// pass the origin check.
    let isGuardedRequest (ctx: HttpContext) =
        Set.contains ctx.Request.Method mutatingMethods
        && ctx.Request.Path.StartsWithSegments(PathString "/api")

    /// Validates the origin of a guarded request; returns true to allow.
    let requestOriginAllowed (allowedHosts: Set<string>) (ctx: HttpContext) =
        originAllowed allowedHosts ctx.Request.Host.Host (headerValue ctx "Origin") (headerValue ctx "Referer")

    /// Parses a comma-separated ALLOWED_ORIGINS value into a set of lowercase hosts.
    let parseAllowedHosts (value: string) =
        if String.IsNullOrWhiteSpace value then Set.empty
        else
            value.Split(',')
            |> Array.choose (fun part ->
                let p = part.Trim()
                if p = "" then None
                else match hostOfUrl p with
                     | Some h -> Some h
                     | None -> Some (p.ToLowerInvariant()))
            |> Set.ofArray
