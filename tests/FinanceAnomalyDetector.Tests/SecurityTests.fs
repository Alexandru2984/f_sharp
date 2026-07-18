module SecurityTests

open Xunit
open FinanceAnomalyDetector

let private allowed = Security.parseAllowedHosts "https://f.micutu.com"

[<Fact>]
let ``same-origin request is allowed via Origin`` () =
    Assert.True(Security.originAllowed allowed "f.micutu.com" (Some "https://f.micutu.com") None)

[<Fact>]
let ``request matching its own host is allowed even without configured hosts`` () =
    Assert.True(Security.originAllowed Set.empty "app.example.com" (Some "https://app.example.com/x") None)

[<Fact>]
let ``sibling subdomain origin is rejected`` () =
    Assert.False(Security.originAllowed allowed "f.micutu.com" (Some "https://evil.micutu.com") None)

[<Fact>]
let ``cross-site origin is rejected`` () =
    Assert.False(Security.originAllowed allowed "f.micutu.com" (Some "https://attacker.example") None)

[<Fact>]
let ``missing origin falls back to a matching referer`` () =
    Assert.True(Security.originAllowed allowed "f.micutu.com" None (Some "https://f.micutu.com/dashboard"))

[<Fact>]
let ``missing origin with a foreign referer is rejected`` () =
    Assert.False(Security.originAllowed allowed "f.micutu.com" None (Some "https://evil.micutu.com/x"))

[<Fact>]
let ``mutating request with neither origin nor referer is rejected`` () =
    Assert.False(Security.originAllowed allowed "f.micutu.com" None None)

[<Fact>]
let ``parseAllowedHosts accepts full origins and bare hosts, case-insensitively`` () =
    let hosts = Security.parseAllowedHosts "https://A.Example.com, bare.host , "
    Assert.True(Set.contains "a.example.com" hosts)
    Assert.True(Set.contains "bare.host" hosts)
    Assert.Equal(2, Set.count hosts)

[<Fact>]
let ``parseAllowedHosts on empty input is empty`` () =
    Assert.True(Set.isEmpty (Security.parseAllowedHosts ""))
