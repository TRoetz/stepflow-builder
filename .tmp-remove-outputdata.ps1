$p = 'C:\Source\stepflow-builder\StepFunctionsApp\StepFlow_Usage_Guide.md'
$c = [IO.File]::ReadAllText($p)
$count = ([regex]::Matches($c, '"OutputData"')).Count
$new = [regex]::Replace($c, '"Type": "Succeed",\s*"OutputData": \{\}', '"Type": "Succeed"')
[IO.File]::WriteAllText($p, $new, (New-Object Text.UTF8Encoding $false))
Write-Host ("removed: {0}" -f $count)
