<#
  Sprawdza na DZIAŁAJĄCYM bocie rzeczy, których nie pokrywają testy jednostkowe.
  Uruchamiać tylko na instancji debug / bazie testowej.

  Przykład:
    pwsh ./smoke.ps1 -AppKey snka_... -DiscordId 123456789012345678
    pwsh ./smoke.ps1 -AppKey snka_... -DiscordId 123456789012345678 -SiteKey <ApiKeys.Key> -WithPacks

  -WithPacks MODYFIKUJE dane: daje użytkownikowi pakiet, otwiera go (nowa karta) i przełącza kartę w talii (2x, wraca do stanu).
#>
param(
    [string]$BaseUrl = "http://localhost:5005",
    [Parameter(Mandatory)][string]$AppKey,
    [Parameter(Mandatory)][string]$DiscordId,
    [string]$SiteKey,
    [switch]$WithPacks
)

$ErrorActionPreference = "Stop"
$script:failed = 0

function Call([string]$Method, [string]$Path, [hashtable]$Headers = @{}, $Body = $null) {
    $params = @{
        Method = $Method; Uri = "$BaseUrl$Path"; Headers = $Headers
        SkipHttpErrorCheck = $true; ContentType = "application/json"
    }
    if ($null -ne $Body) { $params.Body = ConvertTo-Json -InputObject $Body -Depth 10 -Compress }
    $r = Invoke-WebRequest @params
    $json = $null
    try { $json = $r.Content | ConvertFrom-Json } catch { }
    [pscustomobject]@{ Status = [int]$r.StatusCode; Json = $json; Raw = $r.Content }
}

function Check([string]$Name, [bool]$Ok, [string]$Details = "") {
    if ($Ok) { Write-Host "PASS  $Name" -ForegroundColor Green }
    else { Write-Host "FAIL  $Name  $Details" -ForegroundColor Red; $script:failed++ }
}

function Expect([string]$Name, $Response, [int[]]$Codes) {
    Check $Name ($Codes -contains $Response.Status) "(status $($Response.Status): $($Response.Raw))"
}

Write-Host "== Klucze użytkownika ==" -ForegroundColor Cyan

Expect "brak x-app-key -> 401" (Call POST "/api/userkey/discord/$DiscordId") 401
Expect "zły x-app-key -> 403" (Call POST "/api/userkey/discord/$DiscordId" @{ "x-app-key" = "snka_wrong" }) 403
Expect "nieistniejący użytkownik -> 404" (Call POST "/api/userkey/discord/1" @{ "x-app-key" = $AppKey }) 404

$gen = Call POST "/api/userkey/discord/$DiscordId" @{ "x-app-key" = $AppKey }
Expect "generowanie klucza -> 200" $gen 200
$key = $gen.Json.key
Check "klucz ma prefiks snk_" ($key -like "snk_*") "($key)"

$me = Call GET "/api/userkey/me" @{ "x-user-key" = $key }
Expect "me z kluczem -> 200" $me 200
Check "me zwraca właściwego użytkownika" ($me.Json.userId -eq $DiscordId) "(userId: $($me.Json.userId))"

Expect "me ze złym kluczem -> 401" (Call GET "/api/userkey/me" @{ "x-user-key" = "snk_wrong" }) 401
Expect "me bez klucza -> 401" (Call GET "/api/userkey/me") 401
Expect "endpoint Player bez klucza -> 401" (Call PUT "/api/waifu/deck/toggle/card/0") 401

$gen2 = Call POST "/api/userkey/discord/$DiscordId" @{ "x-app-key" = $AppKey }
Expect "ponowne generowanie -> 200" $gen2 200
Expect "stary klucz po ponownym generowaniu -> 401" (Call GET "/api/userkey/me" @{ "x-user-key" = $key }) 401
$key = $gen2.Json.key
Expect "nowy klucz działa -> 200" (Call GET "/api/userkey/me" @{ "x-user-key" = $key }) 200

Write-Host "== Pakiety i przedmioty gracza ==" -ForegroundColor Cyan

$packsRes = Call GET "/api/waifu/boosterpacks" @{ "x-user-key" = $key }
Expect "lista pakietów z kluczem -> 200" $packsRes 200
Check "numery pakietów idą od 1" ((@($packsRes.Json) | ForEach-Object { $_.Number }) -join "," -eq ((1..@($packsRes.Json).Count) -join ",") -or @($packsRes.Json).Count -eq 0) "($($packsRes.Raw))"
Expect "lista pakietów bez klucza -> 401" (Call GET "/api/waifu/boosterpacks") 401

$itemsRes = Call GET "/api/waifu/items" @{ "x-user-key" = $key }
Expect "lista przedmiotów z kluczem -> 200" $itemsRes 200
Write-Host "      pakietów: $(@($packsRes.Json).Count), przedmiotów: $(@($itemsRes.Json).Count)"
Expect "lista przedmiotów bez klucza -> 401" (Call GET "/api/waifu/items") 401

Write-Host "== Stan gracza ==" -ForegroundColor Cyan

foreach ($path in "missions", "limits", "deck", "wishlist", "figures", "tags") {
    Expect "GET $path z kluczem -> 200" (Call GET "/api/waifu/$path" @{ "x-user-key" = $key }) 200
    Expect "GET $path bez klucza -> 401" (Call GET "/api/waifu/$path") 401
}

$deck = (Call GET "/api/waifu/deck" @{ "x-user-key" = $key }).Json
Write-Host "      talia: $(@($deck.Cards).Count) kart, moc $([math]::Round($deck.Power, 2)), status $($deck.PvpDeckStatus), PvP: $($deck.CanPlayPvp)"

Write-Host "== Oznaczenia (tworzy, zmienia i usuwa testowe) ==" -ForegroundColor Cyan

$h = @{ "x-user-key" = $key }
Expect "nazwa ze spacją -> 400" (Call POST "/api/waifu/tags" $h "dwa slowa") 400
$created = Call POST "/api/waifu/tags" $h "smoketest"
Expect "utworzenie oznaczenia -> 201" $created 201
if ($created.Status -eq 201) {
    $tagId = $created.Json.Id
    Expect "duplikat -> 409" (Call POST "/api/waifu/tags" $h "SmokeTest") 409
    Expect "zmiana nazwy -> 200" (Call PUT "/api/waifu/tags/$tagId" $h "smoketest2") 200
    $tags = (Call GET "/api/waifu/tags" $h).Json
    Check "oznaczenie ma nową nazwę" (@($tags.Custom | Where-Object { $_.Id -eq $tagId -and $_.Name -eq "smoketest2" }).Count -eq 1)
    Expect "oznaczenie nieistniejących kart -> 200, 0 kart" (Call POST "/api/waifu/tags/$tagId/cards" $h @(999999999999999)) 200
    Expect "usunięcie -> 200" (Call DELETE "/api/waifu/tags/$tagId" $h) 200
    Expect "ponowne usunięcie -> 404" (Call DELETE "/api/waifu/tags/$tagId" $h) 404
}
Expect "cudze/nieistniejące oznaczenie na kartach -> 404" (Call POST "/api/waifu/tags/999999999999999/cards" $h @(999999999999999)) 404
Write-Host "      w konsoli bota powinny pojawić się linie 'API: u$DiscordId app:... POST/PUT/DELETE ...'"

Write-Host "== Lista kart (limit 4000, nazwy bez .Result) ==" -ForegroundColor Cyan

$watch = [Diagnostics.Stopwatch]::StartNew()
$cards = Call POST "/api/waifu/total/cards/0/5000" @{} @{}
$watch.Stop()
Expect "total/cards -> 200" $cards 200
Check "total/cards zwraca max 4000 kart" ($cards.Json.Cards.Count -le 4000) "(zwrócono $($cards.Json.Cards.Count))"
Write-Host "      $($cards.Json.Cards.Count) kart z $($cards.Json.TotalCards) w $($watch.ElapsedMilliseconds) ms"

if ($WithPacks) {
    if (-not $SiteKey) { throw "-WithPacks wymaga -SiteKey (klucz z ApiKeys)" }

    Write-Host "== Pakiety i talia przez klucz użytkownika ==" -ForegroundColor Cyan

    $token = (Call POST "/api/token" @{} $SiteKey).Json.token
    $site = @{ Authorization = "Bearer $token" }

    $pack = @(@{ Name = "smoke-test"; Count = 1; Rarity = "E"; Tradable = $true; Pool = @{ Type = "random" } })
    Expect "dodanie pakietu (Site) -> 200" (Call POST "/api/waifu/discord/$DiscordId/boosterpack" $site $pack) 200
    Start-Sleep -Seconds 2

    $packs = @((Call GET "/api/user/discord/$DiscordId" $site).Json.GameDeck.BoosterPacks)
    $packCount = $packs.Count
    $packNumber = 0
    for ($i = 0; $i -lt $packs.Count; $i++) { if ($packs[$i].Name -eq "smoke-test") { $packNumber = $i + 1 } }
    Check "użytkownik ma pakiet smoke-test" ($packNumber -gt 0) "(pakietów: $packCount)"
    if ($packNumber -eq 0) { throw "nie znaleziono pakietu smoke-test" }

    # dwa równoległe otwarcia tego samego pakietu: dokładnie jedno może się udać
    $client = [System.Net.Http.HttpClient]::new()
    $requests = 1..2 | ForEach-Object {
        $m = [System.Net.Http.HttpRequestMessage]::new("POST", "$BaseUrl/api/waifu/boosterpack/open/$packNumber")
        $m.Headers.Add("x-user-key", $key)
        $client.SendAsync($m)
    }
    [System.Threading.Tasks.Task]::WaitAll($requests)
    $statuses = $requests | ForEach-Object { [int]$_.Result.StatusCode }
    $okCount = @($statuses | Where-Object { $_ -eq 200 }).Count
    Check "równoległe otwarcie: jedno 200, drugie 409/404" ($okCount -eq 1 -and @($statuses | Where-Object { $_ -in 404, 409 }).Count -eq 1) "(statusy: $($statuses -join ', '))"

    $opened = ($requests | Where-Object { [int]$_.Result.StatusCode -eq 200 } | Select-Object -First 1).Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json
    $wid = @($opened)[0].Id
    Check "otwarty pakiet zwrócił kartę" ($null -ne $wid) "($opened)"

    $after = @((Call GET "/api/user/discord/$DiscordId" $site).Json.GameDeck.BoosterPacks).Count
    Check "pakiet zużyty dokładnie raz" ($after -eq $packCount - 1) "(przed: $packCount, po: $after)"

    if ($null -ne $wid) {
        $powerBefore = (Call GET "/api/user/discord/$DiscordId" $site).Json.GameDeck.DeckPower
        Expect "przełączenie karty -> 200" (Call PUT "/api/waifu/deck/toggle/card/$wid" @{ "x-user-key" = $key }) 200
        $powerMid = (Call GET "/api/user/discord/$DiscordId" $site).Json.GameDeck.DeckPower
        Check "moc talii zmieniła się po przełączeniu" ($powerMid -ne $powerBefore) "(przed: $powerBefore, po: $powerMid)"

        Expect "przełączenie z powrotem -> 200" (Call PUT "/api/waifu/deck/toggle/card/$wid" @{ "x-user-key" = $key }) 200
        $powerAfter = (Call GET "/api/user/discord/$DiscordId" $site).Json.GameDeck.DeckPower
        Check "moc talii wróciła do stanu sprzed testu" ([math]::Abs($powerAfter - $powerBefore) -lt 0.001) "(przed: $powerBefore, po: $powerAfter)"
    }

    Expect "przełączenie nieistniejącej karty -> 404" (Call PUT "/api/waifu/deck/toggle/card/1" @{ "x-user-key" = $key }) 404
}

Write-Host "== Unieważnienie ==" -ForegroundColor Cyan

Expect "unieważnienie klucza -> 200" (Call DELETE "/api/userkey/discord/$DiscordId" @{ "x-app-key" = $AppKey }) 200
Expect "klucz po unieważnieniu -> 401" (Call GET "/api/userkey/me" @{ "x-user-key" = $key }) 401
Expect "ponowne unieważnienie -> 404" (Call DELETE "/api/userkey/discord/$DiscordId" @{ "x-app-key" = $AppKey }) 404

Write-Host ""
if ($script:failed -eq 0) { Write-Host "Wszystko OK" -ForegroundColor Green }
else { Write-Host "Niepowodzeń: $script:failed" -ForegroundColor Red; exit 1 }
