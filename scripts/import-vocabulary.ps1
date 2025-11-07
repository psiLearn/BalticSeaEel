param(
    [string]$CsvPath = "vocabularies/french_vocabularies_balticsea.csv",
    [string]$Connection = "Host=localhost;Port=5432;Username=eel;Password=eel;Database=eel"
)

function Parse-ConnectionString([string]$conn) {
    $map = @{}
    foreach ($segment in $conn.Split(";", [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $kv = $segment.Split("=", 2)
        if ($kv.Length -eq 2) {
            $map[$kv[0].Trim()] = $kv[1].Trim()
        }
    }
    return $map
}

if (-not (Test-Path $CsvPath)) {
    throw "CSV file '$CsvPath' not found."
}

$parts = Parse-ConnectionString $Connection
$host = $parts["Host"]
$port = $parts["Port"]
$user = $parts["Username"]
$password = $parts["Password"]
$database = $parts["Database"]

if (-not $host -or -not $user -or -not $database) {
    throw "Connection string must include Host, Username, Password, and Database."
}

if (-not $port) { $port = "5432" }

$resolvedCsv = (Resolve-Path $CsvPath).Path

Write-Host "Importing vocabulary from $resolvedCsv into $database@$host..."

$env:PGPASSWORD = $password
try {
    $copyCommand = "\copy vocabulary(topic, language1, language2, example) FROM '$resolvedCsv' WITH (FORMAT csv, HEADER true)"
    psql -h $host -p $port -U $user -d $database -c $copyCommand
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
