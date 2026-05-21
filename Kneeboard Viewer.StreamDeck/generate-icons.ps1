# Generates flat white glyph PNGs for the Stream Deck plugin.
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot 'Icons'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-Icon([string]$name, [scriptblock]$draw) {
    foreach ($size in 72, 144) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, ($size * 0.09))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        & $draw $g $pen $brush $size
        $suffix = if ($size -eq 144) { '@2x' } else { '' }
        $bmp.Save((Join-Path $outDir "$name$suffix.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $brush.Dispose()
    }
}

# Chevron pointing right or left; $count = 1 (page) or 2 (tab).
function Draw-Chevrons($g, $pen, $s, [bool]$right, [int]$count) {
    [float]$h      = $s / 2.0
    [float]$w      = $s * 0.18
    [float]$spread = $s * 0.16
    [float]$startX = if ($count -eq 2) { $s * 0.36 } else { $s * 0.5 }
    for ($i = 0; $i -lt $count; $i++) {
        [float]$cx   = $startX + ($i * $s * 0.22)
        [float]$tip  = if ($right) { $cx + $w } else { $cx - $w }
        [float]$back = if ($right) { $cx - $w } else { $cx + $w }
        [float]$yTop = $h - $spread
        [float]$yBot = $h + $spread
        $g.DrawLines($pen, @(
            (New-Object System.Drawing.PointF($back, $yTop)),
            (New-Object System.Drawing.PointF($tip,  $h)),
            (New-Object System.Drawing.PointF($back, $yBot))))
    }
}

New-Icon 'nextpage' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 1 }
New-Icon 'prevpage' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $false 1 }
New-Icon 'nexttab'  { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }
New-Icon 'prevtab'  { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $false 2 }

# Refresh: open circle with an arrowhead.
New-Icon 'reload' { param($g,$pen,$brush,$s)
    [float]$m   = $s * 0.28
    [float]$dia = $s - 2*$m
    $g.DrawArc($pen, $m, $m, $dia, $dia, 40, 280)
    [float]$ax  = $s - $m
    [float]$ay  = $s * 0.32
    [float]$ax1 = $ax - $s*0.10
    [float]$ay1 = $ay
    [float]$ax2 = $ax
    [float]$ay2 = $ay - $s*0.02
    [float]$ax3 = $ax + $s*0.02
    [float]$ay3 = $ay + $s*0.10
    $g.DrawLines($pen, @(
        (New-Object System.Drawing.PointF($ax1, $ay1)),
        (New-Object System.Drawing.PointF($ax2, $ay2)),
        (New-Object System.Drawing.PointF($ax3, $ay3)))) }

# Run: filled play triangle.
New-Icon 'run' { param($g,$pen,$brush,$s)
    [float]$x1 = $s*0.36; [float]$y1 = $s*0.28
    [float]$x2 = $s*0.72; [float]$y2 = $s*0.5
    [float]$x3 = $s*0.36; [float]$y3 = $s*0.72
    $g.FillPolygon($brush, @(
        (New-Object System.Drawing.PointF($x1, $y1)),
        (New-Object System.Drawing.PointF($x2, $y2)),
        (New-Object System.Drawing.PointF($x3, $y3)))) }

# Quit: power symbol (arc gap at top + vertical stroke).
New-Icon 'quit' { param($g,$pen,$brush,$s)
    [float]$m   = $s * 0.28
    [float]$dia = $s - 2*$m
    $g.DrawArc($pen, $m, $m, $dia, $dia, -60, 300)
    [float]$cx  = $s/2
    [float]$ly1 = $s*0.20
    [float]$ly2 = $s*0.5
    $g.DrawLine($pen, $cx, $ly1, $cx, $ly2) }

# Plugin / category icon: reuse the double chevron as a generic mark.
New-Icon 'plugin'   { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }
New-Icon 'category' { param($g,$pen,$brush,$s) Draw-Chevrons $g $pen $s $true 2 }

Write-Host "Icons written to $outDir"
