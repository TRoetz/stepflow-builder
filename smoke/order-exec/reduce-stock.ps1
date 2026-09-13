# ReduceStock - finalize a successfully AI-processed order: verify + fix the invoice, then decrement stock.
# Input: { orderId, customerName, lines[{productId,name,price,qty,lineTotal}], total, file, ai }
# Runs AFTER AskAi succeeds so a failed AI run leaves no stock side effects; passes `ai` through to MarkProcessed.
$ErrorActionPreference = "Stop"

$file = [string]$input_data.file
$dbPath = Join-Path "C:\temp\order-exec" "orders.db"
if (-not (Test-Path $dbPath)) { throw ("database not found: " + $dbPath) }

# Verify the agent actually wrote the invoice before applying any side effects.
$invoice = Join-Path "C:\temp\order-exec\output" ("invoice-" + [string]$input_data.orderId + ".txt")
if (-not (Test-Path $invoice)) { throw ("invoice not found: " + $invoice) }

# Enforce the real UTC date on the invoice - the model hallucinates dates despite prompt instructions.
$text = Get-Content $invoice -Raw
$nowUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
Set-Content -Path $invoice -Value ([regex]::Replace($text, "(?m)^Date:.*$", ("Date: " + $nowUtc))) -Encoding UTF8

$reduced = @()
foreach ($line in @($input_data.lines)) {
    $pid_ = [int]$line.productId
    $qty = [int]$line.qty
    $before = 0; $after = 0
    try {
        $before = [int](& python -c "import sqlite3;c=sqlite3.connect(r'$dbPath');print(c.execute('SELECT stock FROM products WHERE id=$pid_').fetchone()[0])" 2>&1 | Out-String).Trim()
        & python -c "import sqlite3;c=sqlite3.connect(r'$dbPath');c.execute('UPDATE products SET stock=stock-$qty WHERE id=$pid_ AND stock>=$qty');c.commit()" 2>&1 | Out-Null
        $after = [int](& python -c "import sqlite3;c=sqlite3.connect(r'$dbPath');print(c.execute('SELECT stock FROM products WHERE id=$pid_').fetchone()[0])" 2>&1 | Out-String).Trim()
    } catch {
        throw ("stock update failed for product ${pid_}: " + $_.Exception.Message)
    }
    if ($after -ge $before) { throw ("stock not decremented for product $pid_ (before=$before after=$after); refusing to continue") }
    $reduced += [pscustomobject]@{ productId=$pid_; name=[string]$line.name; qty=$qty; before=$before; after=$after }
}

return [pscustomobject]@{ orderId=[string]$input_data.orderId; customerName=[string]$input_data.customerName; lines=@($input_data.lines); total=[double]$input_data.total; file=$file; reduced=@($reduced); ai=$input_data.ai }
