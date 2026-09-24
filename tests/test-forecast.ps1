param(
    [string]$ExePath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'CodexQuotaBar.exe'),
    [switch]$Live
)

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExePath))
$forecastType = $assembly.GetType('CodexQuotaBar.TiboForecast', $true)
$parseMethod = $forecastType.GetMethod('Parse', [Reflection.BindingFlags]'Static,NonPublic')
$applyFeedMethod = $forecastType.GetMethod('ApplyFeed', [Reflection.BindingFlags]'Instance,NonPublic')
$fieldFlags = [Reflection.BindingFlags]'Instance,NonPublic'

function Parse-Forecast([string]$Json, [string]$Now) {
    $instant = [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
    return $parseMethod.Invoke($null, @($Json, $instant))
}

function Apply-Feed($Value, [string]$Json, [string]$Now) {
    $instant = [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
    $applyFeedMethod.Invoke($Value, @($Json, $instant)) | Out-Null
}

function Read-Field($Value, [string]$Name) {
    return $forecastType.GetField($Name, $fieldFlags).GetValue($Value)
}

function Assert-Equal($Actual, $Expected, [string]$Name) {
    if ($Actual -ne $Expected) { throw "$Name expected '$Expected', got '$Actual'." }
}

$announcement = @'
{"mode":"announced","updated_at":"2026-09-22T07:25:19Z","probabilities":{"rounded_24h":20},"official_signal":{"url":"https://x.com/thsottiaux/status/2102254445082116335","unknown":"ignored","at":"2026-09-22T04:31:32Z","window":{"label":"end of Tuesday","end_at":"2026-09-23T06:59:59Z","target_kind":"deadline","target_at":"2026-09-23T06:59:59Z"}}}
'@
$result = Parse-Forecast $announcement '2026-09-22T07:30:00Z'
$announcementResult = $result
Assert-Equal (Read-Field $result 'IsFresh') $true 'fresh announcement'
Assert-Equal (Read-Field $result 'HasAnnouncement') $true 'announcement detected'
Assert-Equal (Read-Field $result 'AnnouncementWindow') 'end of Tuesday' 'announcement window'
Assert-Equal (Read-Field $result 'AnnouncementTimeUtc') $null 'deadline is not an exact reset time'
Assert-Equal (Read-Field $result 'Probability24h') 20 'rounded probability'
Assert-Equal (Read-Field $result 'SourceUrl') 'https://x.com/thsottiaux/status/2102254445082116335' 'source URL'

$probabilityOnly = '{"updated_at":"2026-09-24T03:46:00Z","probabilities":{"rounded_24h":20},"official_signal":null}'
$result = Parse-Forecast $probabilityOnly '2026-09-24T03:50:00Z'
$probabilityResult = Parse-Forecast $probabilityOnly '2026-09-24T03:50:00Z'
Assert-Equal (Read-Field $result 'HasAnnouncement') $false 'no announcement'
Assert-Equal (Read-Field $result 'Probability24h') 20 'probability-only result'

$feed = @'
{"fetched_at":"2026-09-24T03:43:52Z","stale":false,"events":[{"group":"credits","reset_kind":"banked","announced_at":"2026-09-22T18:23:37Z","url":"https://x.com/thsottiaux/status/2102463847714247142","unknown":"ignored"},{"group":"reset","announcement_state":"none","announced_at":"2026-09-22T04:31:32Z","url":"https://x.com/thsottiaux/status/2102254445082116335"}]}
'@
Apply-Feed $result $feed '2026-09-24T03:50:00Z'
$bankedResult = $result
Assert-Equal (Read-Field $result 'LatestEventKind') 'banked' 'banked reset result'
Assert-Equal (Read-Field $result 'SourceUrl') 'https://x.com/thsottiaux/status/2102463847714247142' 'banked reset source'

$postResetResult = Parse-Forecast $probabilityOnly '2026-09-24T03:50:00Z'
$completedFeed = '{"fetched_at":"2026-09-24T03:43:52Z","stale":false,"events":[{"group":"reset","announcement_state":"announced","announced_at":"2026-09-23T18:23:37Z","url":"https://x.com/thsottiaux/status/completed"}]}'
Apply-Feed $postResetResult $completedFeed '2026-09-24T03:50:00Z'
Assert-Equal (Read-Field $postResetResult 'LatestEventKind') 'reset' 'completed reset event parsed'

$expiredSignal = '{"updated_at":"2026-09-23T07:05:00Z","probabilities":{"rounded_24h":18},"official_signal":{"url":"https://x.com/thsottiaux/status/1","window":{"label":"end of Tuesday","end_at":"2026-09-23T06:59:59Z","target_kind":"deadline"}}}'
$result = Parse-Forecast $expiredSignal '2026-09-23T07:10:00Z'
$expiredResult = $result
Assert-Equal (Read-Field $result 'HasAnnouncement') $false 'expired announcement'
Assert-Equal (Read-Field $result 'Probability24h') 18 'expired announcement keeps probability'

$stale = '{"updated_at":"2026-09-22T06:00:00Z","probabilities":{"rounded_24h":25}}'
$result = Parse-Forecast $stale '2026-09-22T07:30:00Z'
Assert-Equal (Read-Field $result 'IsFresh') $false 'stale payload'

$failed = $false
try { Parse-Forecast '{bad json' '2026-09-22T07:30:00Z' | Out-Null }
catch { $failed = $true }
Assert-Equal $failed $true 'malformed JSON rejected'

$barType = $assembly.GetType('CodexQuotaBar.Bar', $true)
$bar = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($barType)
$barType.GetField('chinese', $fieldFlags).SetValue($bar, $false)
$forecastField = $barType.GetField('forecast', $fieldFlags)
$labelMethod = $barType.GetMethod('ForecastLabel', [Reflection.BindingFlags]'Instance,NonPublic')
$expiresField = $forecastType.GetField('ExpiresAt', $fieldFlags)
$announcementEndsField = $forecastType.GetField('AnnouncementEndsAtUtc', $fieldFlags)
$expiresField.SetValue($announcementResult, [DateTimeOffset]::UtcNow.AddHours(1))
$announcementEndsField.SetValue($announcementResult, [DateTimeOffset]::UtcNow.AddMinutes(5))
$forecastField.SetValue($bar, $announcementResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '100% reset' 'active announcement display label'
$barType.GetField('chinese', $fieldFlags).SetValue($bar, $true)
Assert-Equal ($labelMethod.Invoke($bar, @())) '100% reset' 'same compact label in Chinese UI'
$expiresField.SetValue($probabilityResult, [DateTimeOffset]::UtcNow.AddHours(1))
$forecastField.SetValue($bar, $probabilityResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '20% reset' 'probability-only display label'
$expiresField.SetValue($bankedResult, [DateTimeOffset]::UtcNow.AddHours(1))
$forecastField.SetValue($bar, $bankedResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '20% reset' 'historical banked event does not block probability'
$expiresField.SetValue($postResetResult, [DateTimeOffset]::UtcNow.AddHours(1))
$forecastField.SetValue($bar, $postResetResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '20% reset' 'completed reset event does not block probability'
$expiresField.SetValue($expiredResult, [DateTimeOffset]::UtcNow.AddHours(1))
$forecastField.SetValue($bar, $expiredResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '18% reset' 'expired announcement returns to probability'
$forecastField.SetValue($bar, $null)
Assert-Equal ($labelMethod.Invoke($bar, @())) '--% reset' 'unavailable data has no fabricated probability'

$newerAnnouncement = '{"updated_at":"2026-09-22T20:35:00Z","probabilities":{"rounded_24h":93},"official_signal":{"url":"https://x.com/thsottiaux/status/new","at":"2026-09-22T20:31:00Z","window":{"label":"end of Tuesday","end_at":"2026-09-23T06:59:59Z","target_kind":"deadline"}}}'
$newerResult = Parse-Forecast $newerAnnouncement '2026-09-22T20:40:00Z'
$olderFeed = '{"fetched_at":"2026-09-22T20:36:00Z","stale":false,"events":[{"group":"credits","reset_kind":"banked","announced_at":"2026-09-22T18:23:37Z","url":"https://x.com/thsottiaux/status/old"}]}'
Apply-Feed $newerResult $olderFeed '2026-09-22T20:40:00Z'
$expiresField.SetValue($newerResult, [DateTimeOffset]::UtcNow.AddHours(1))
$announcementEndsField.SetValue($newerResult, [DateTimeOffset]::UtcNow.AddMinutes(5))
$forecastField.SetValue($bar, $newerResult)
Assert-Equal ($labelMethod.Invoke($bar, @())) '100% reset' 'newer announcement takes priority'
Assert-Equal (Read-Field $newerResult 'SourceUrl') 'https://x.com/thsottiaux/status/new' 'newer announcement source'
$announcementEndsField.SetValue($newerResult, [DateTimeOffset]::UtcNow.AddMinutes(-1))
Assert-Equal ($labelMethod.Invoke($bar, @())) '93% reset' 'announcement window end immediately returns to probability'

if ($Live) {
    $clientType = $assembly.GetType('CodexQuotaBar.TiboForecastClient', $true)
    $client = [Activator]::CreateInstance($clientType, $true)
    try {
        $readMethod = $clientType.GetMethod('Read', [Reflection.BindingFlags]'Instance,NonPublic')
        $liveResult = $readMethod.Invoke($client, @()).GetAwaiter().GetResult()
        Write-Output ("Live: event={0}; announcement={1}; probability={2}; source={3}" -f
            (Read-Field $liveResult 'LatestEventKind'),
            (Read-Field $liveResult 'HasAnnouncement'),
            (Read-Field $liveResult 'Probability24h'),
            (Read-Field $liveResult 'SourceUrl'))
    }
    finally { $client.Dispose() }
}

Write-Output 'Forecast fixtures passed.'
