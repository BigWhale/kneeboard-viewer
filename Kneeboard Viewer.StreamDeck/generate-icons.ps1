# Generates the Stream Deck plugin icons.
#  - Page keys: left/right arrows (linear page flipping)
#  - Tab keys: double-chevron guillemets (jump between sections)
#  - Run: the Kneeboard Viewer application icon
#  - Refresh/Quit: drawn glyphs; plugin/category: a generic mark
# White glyphs on transparent, two sizes each (name.png 72px, name@2x.png 144px).
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore   # WPF imaging decodes PNG-compressed .ico frames

$outDir = Join-Path $PSScriptRoot 'Icons'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$appIco = Join-Path $PSScriptRoot '..\Kneeboard Viewer\app.ico'
if (-not (Test-Path $appIco)) { throw "Application icon not found at $appIco" }

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

# Centered text glyph. $text is a single Unicode character; $factor scales the
# font size relative to the icon size. Segoe UI Symbol carries the arrows and
# guillemets we use.
function Draw-Glyph($g, $brush, $s, [string]$text, [double]$factor) {
    [float]$fontSize = $s * $factor
    $font = New-Object System.Drawing.Font('Segoe UI Symbol', $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    [float]$fs = $s
    $rect = New-Object System.Drawing.RectangleF(([float]0), ([float]0), $fs, $fs)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.DrawString($text, $font, $brush, $rect, $fmt)
    $font.Dispose(); $fmt.Dispose()
}

# Renders the largest frame of the app icon scaled to fill the canvas. Uses the
# WPF decoder because app.ico stores its larger frames as PNG, which the legacy
# System.Drawing.Icon.ToBitmap cannot decode.
function Draw-AppIcon($g, $s, $icoPath) {
    $stream = [System.IO.File]::OpenRead($icoPath)
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::None,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($frame))
        $ms = New-Object System.IO.MemoryStream
        $enc.Save($ms)
        $ms.Position = 0
        $src = New-Object System.Drawing.Bitmap($ms)
    } finally {
        $stream.Dispose()
    }
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $dst = New-Object System.Drawing.Rectangle(0, 0, [int]$s, [int]$s)
    $g.DrawImage($src, $dst)
    $src.Dispose()
}

# Page navigation: left/right arrows.
New-Icon 'nextpage' { param($g,$pen,$brush,$s) Draw-Glyph $g $brush $s ([char]0x2192) 0.60 }  # right arrow
New-Icon 'prevpage' { param($g,$pen,$brush,$s) Draw-Glyph $g $brush $s ([char]0x2190) 0.60 }  # left arrow

# Tab navigation: double-chevron guillemets.
New-Icon 'nexttab'  { param($g,$pen,$brush,$s) Draw-Glyph $g $brush $s ([char]0x00BB) 0.80 }  # >>
New-Icon 'prevtab'  { param($g,$pen,$brush,$s) Draw-Glyph $g $brush $s ([char]0x00AB) 0.80 }  # <<

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

# Run: the Kneeboard Viewer application icon.
New-Icon 'run' { param($g,$pen,$brush,$s) Draw-AppIcon $g $s $appIco }

# Quit: power symbol (arc gap at top + vertical stroke).
New-Icon 'quit' { param($g,$pen,$brush,$s)
    [float]$m   = $s * 0.28
    [float]$dia = $s - 2*$m
    $g.DrawArc($pen, $m, $m, $dia, $dia, -60, 300)
    [float]$cx  = $s/2
    [float]$ly1 = $s*0.20
    [float]$ly2 = $s*0.5
    $g.DrawLine($pen, $cx, $ly1, $cx, $ly2) }

# Plugin / category icon: the Kneeboard Viewer application icon (this is the
# icon Stream Deck shows next to the action category and in the plugin list).
New-Icon 'plugin'   { param($g,$pen,$brush,$s) Draw-AppIcon $g $s $appIco }
New-Icon 'category' { param($g,$pen,$brush,$s) Draw-AppIcon $g $s $appIco }

Write-Host "Icons written to $outDir"
