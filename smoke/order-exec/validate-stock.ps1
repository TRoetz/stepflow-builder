# ValidateStock - validate order lines against C:\temp\order-exec\orders.db.
# Input: { file, import: { enrichedRows: [ {OrderId, ProductId, Qty, CustomerName} ] } }
# Emits: { valid, errors[], orderId, customerName, lines[{productId,name,price,qty,lineTotal}], total, file }
$ErrorActionPreference = "Stop"

$file = [string]$input_data.file
$rows = @()
if ($input_data.import -and $input_data.import.enrichedRows) { $rows = @($input_data.import.enrichedRows) }
if ($rows.Count -eq 0) {
    return [pscustomobject]@{ valid=$false; errors=@("no rows in import result"); orderId=""; customerName=""; lines=@(); total=0.0; file=$file }
}

$orderId = [string]$rows[0].OrderId
$customerName = [string]$rows[0].CustomerName

# Parse and merge line items (sum qty per product).
$lines = @{}
$errors = @()
foreach ($r in $rows) {
    $pid_ = 0; $qty = 0
    if (-not [int]::TryParse([string]$r.ProductId, [ref]$pid_)) { $errors += ("row: ProductId not numeric: " + [string]$r.ProductId); continue }
    if (-not [int]::TryParse([string]$r.Qty, [ref]$qty) -or $qty -le 0) { $errors += ("row for product ${pid_}: Qty must be a positive integer"); continue }
    if ($lines.ContainsKey($pid_)) { $lines[$pid_] = $lines[$pid_] + $qty } else { $lines[$pid_] = $qty }
}

# Fetch all referenced products in one query.
$ids = (($lines.Keys | ForEach-Object { [string]$_ }) -join ",")
$dbq = Join-Path "C:\temp\order-exec" "dbq.py"
$sqlFile = Join-Path $env:TEMP ("sf_ordv_" + [guid]::NewGuid().ToString("N") + ".sql")
Set-Content -Path $sqlFile -Value ("SELECT id, name, price, stock, category, description FROM products WHERE id IN (" + $ids + ")") -Encoding UTF8
$pyOut = (& python $dbq $sqlFile 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw ("catalog query failed: " + $pyOut) }
$db = $pyOut | ConvertFrom-Json
$byId = @{}
foreach ($p in @($db.rows)) { $byId[[int]$p.id] = $p }

$outLines = @()
$total = 0.0
foreach ($k in $lines.Keys) {
    if (-not $byId.ContainsKey([int]$k)) { $errors += ("product " + [string]$k + " not found"); continue }
    $p = $byId[[int]$k]
    if ([int]$p.stock -lt $lines[$k]) {
        $errors += ("insufficient stock for product " + [string]$k + " (" + $p.name + "): have " + $p.stock + ", need " + $lines[$k])
        continue
    }
    $lineTotal = [math]::Round([double]$p.price * $lines[$k], 2)
    $total += $lineTotal
    $outLines += [pscustomobject]@{ productId=[int]$k; name=[string]$p.name; price=[double]$p.price; qty=$lines[$k]; lineTotal=$lineTotal }
}

return [pscustomobject]@{
    valid = ($errors.Count -eq 0)
    errors = @($errors)
    orderId = $orderId
    customerName = $customerName
    lines = @($outLines)
    total = [math]::Round($total, 2)
    file = $file
}
