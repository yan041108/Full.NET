function Read-ReportingCutoverQuery {
    param(
        [string] $Provider, [string] $Server, [string] $Database,
        [string] $MySqlLoginPath, [string] $MySqlCaFile, [int] $MySqlPort,
        [string] $QueryFile, [string] $EvidenceQuery
    )

    $PSNativeCommandUseErrorActionPreference = $false
    if ($Provider -eq 'SqlServer') {
        $arguments = @('-S', $Server, '-d', $Database, '-N', '-b', '-h', '-1', '-W', '-l', '10', '-t', '15')
        if ($EvidenceQuery) { $arguments += @('-x', '-w', '2000', '-Q', $EvidenceQuery) }
        else { $arguments += @('-i', $QueryFile) }
        $result = & sqlcmd @arguments 2>$null
    } else {
        if (-not $MySqlLoginPath -or -not $MySqlCaFile -or -not (Test-Path -LiteralPath $MySqlCaFile -PathType Leaf)) {
            throw 'Reporting cutover requires a MySQL login path and a trusted CA file.'
        }
        $query = if ($EvidenceQuery) { $EvidenceQuery } else { Get-Content -LiteralPath $QueryFile -Raw }
        $result = & mysql --no-defaults "--login-path=$MySqlLoginPath" --protocol=TCP "--host=$Server" "--port=$MySqlPort" `
            "--database=$Database" --ssl-mode=VERIFY_IDENTITY "--ssl-ca=$MySqlCaFile" --batch --raw --skip-column-names `
            --connect-timeout=10 --skip-reconnect --execute $query 2>$null
    }
    if ($LASTEXITCODE -ne 0) { throw 'Reporting cutover could not read the export state from the configured database.' }
    return ($result -join "`n").Trim()
}
