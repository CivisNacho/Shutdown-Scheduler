Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\Assets"
$assetsDir = [System.IO.Path]::GetFullPath($assetsDir)

$colors = @{
    Panel = [System.Drawing.Color]::FromArgb(255, 16, 17, 20)
    PanelEdge = [System.Drawing.Color]::FromArgb(255, 43, 47, 54)
    Accent = [System.Drawing.Color]::FromArgb(255, 122, 146, 173)
    Paper = [System.Drawing.Color]::FromArgb(255, 243, 240, 233)
    Shadow = [System.Drawing.Color]::FromArgb(130, 0, 0, 0)
}

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Initialize-Graphics {
    param([System.Drawing.Graphics]$Graphics)

    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
}

function New-RectF {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height
    )

    return [System.Drawing.RectangleF]::new($X, $Y, $Width, $Height)
}

function Draw-SchedulerIcon {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Size,
        [bool]$TransparentBackground,
        [bool]$WideSurface
    )

    $stroke = [Math]::Max(3, $Size * 0.09)
    $center = $Size / 2.0

    if (-not $TransparentBackground) {
        $margin = $Size * 0.08
        $radius = $Size * 0.22
        $shadowRect = New-RectF $margin ($margin + ($Size * 0.022)) ($Size - ($margin * 2)) ($Size - ($margin * 2))
        $shadowPath = New-RoundedRectPath $shadowRect.X $shadowRect.Y $shadowRect.Width $shadowRect.Height $radius
        $shadowBrush = [System.Drawing.SolidBrush]::new($colors.Shadow)
        $Graphics.FillPath($shadowBrush, $shadowPath)
        $shadowBrush.Dispose()
        $shadowPath.Dispose()

        $cardRect = New-RectF $margin $margin ($Size - ($margin * 2)) ($Size - ($margin * 2))
        $cardPath = New-RoundedRectPath $cardRect.X $cardRect.Y $cardRect.Width $cardRect.Height $radius
        $cardBrush = [System.Drawing.SolidBrush]::new($colors.Panel)
        $cardBorder = [System.Drawing.Pen]::new($colors.PanelEdge, [Math]::Max(1.5, $Size * 0.018))
        $Graphics.FillPath($cardBrush, $cardPath)
        $Graphics.DrawPath($cardBorder, $cardPath)
        $cardBrush.Dispose()
        $cardBorder.Dispose()
        $cardPath.Dispose()
    }

    $glyphPadding = if ($TransparentBackground) { $Size * 0.14 } else { $Size * 0.2 }
    $ringRect = New-RectF $glyphPadding ($glyphPadding + ($Size * 0.01)) ($Size - ($glyphPadding * 2)) ($Size - ($glyphPadding * 2))
    $accentPen = [System.Drawing.Pen]::new($colors.Accent, $stroke)
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paperPen = [System.Drawing.Pen]::new($colors.Paper, $stroke)
    $paperPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paperPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $Graphics.DrawArc($accentPen, $ringRect.X, $ringRect.Y, $ringRect.Width, $ringRect.Height, 214, 180)
    $Graphics.DrawArc($paperPen, $ringRect.X, $ringRect.Y, $ringRect.Width, $ringRect.Height, 34, 74)

    $notchWidth = $stroke * 0.68
    $notchHeight = $Size * 0.16
    $notchBrush = [System.Drawing.SolidBrush]::new($colors.Paper)
    $Graphics.FillRectangle(
        $notchBrush,
        $center - ($notchWidth / 2),
        $ringRect.Y - ($notchHeight * 0.08),
        $notchWidth,
        $notchHeight)

    $handPen = [System.Drawing.Pen]::new($colors.Paper, [Math]::Max(2.5, $stroke * 0.34))
    $handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawLine($handPen, $center, $center, $center, $center - ($ringRect.Height * 0.2))
    $Graphics.DrawLine($handPen, $center, $center, $center + ($ringRect.Width * 0.17), $center + ($ringRect.Height * 0.12))

    $dotBrush = [System.Drawing.SolidBrush]::new($colors.Paper)
    $dotSize = $stroke * 0.34
    $Graphics.FillEllipse($dotBrush, $center - ($dotSize / 2), $center - ($dotSize / 2), $dotSize, $dotSize)

    if (-not $TransparentBackground -and -not $WideSurface) {
        $tickPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(110, $colors.PanelEdge), [Math]::Max(1.2, $Size * 0.012))
        $tickY = $ringRect.Bottom - ($stroke * 0.05)
        $tickGap = $stroke * 0.5
        for ($i = -1; $i -le 1; $i++) {
            $tickX = $center + ($i * $tickGap * 1.6)
            $Graphics.DrawLine($tickPen, $tickX, $tickY, $tickX, $tickY + ($Size * 0.06))
        }
        $tickPen.Dispose()
    }

    $accentPen.Dispose()
    $paperPen.Dispose()
    $notchBrush.Dispose()
    $handPen.Dispose()
    $dotBrush.Dispose()
}

function Save-SquareIcon {
    param(
        [string]$Path,
        [int]$Size,
        [bool]$TransparentBackground
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Initialize-Graphics $graphics
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-SchedulerIcon $graphics $Size $TransparentBackground $false
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Save-WideLogo {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Initialize-Graphics $graphics
    $graphics.Clear($colors.Panel)

    $cardPen = [System.Drawing.Pen]::new($colors.PanelEdge, 2)
    $graphics.DrawRectangle($cardPen, 1, 1, $Width - 3, $Height - 3)

    $iconSize = [Math]::Min($Height * 0.72, $Width * 0.24)
    $iconBitmap = [System.Drawing.Bitmap]::new([int]$iconSize, [int]$iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
    Initialize-Graphics $iconGraphics
    $iconGraphics.Clear([System.Drawing.Color]::Transparent)
    Draw-SchedulerIcon $iconGraphics $iconBitmap.Width $false $true

    $iconX = [int](($Width * 0.08))
    $iconY = [int](($Height - $iconBitmap.Height) / 2)
    $graphics.DrawImage($iconBitmap, $iconX, $iconY, $iconBitmap.Width, $iconBitmap.Height)

    $titleBrush = [System.Drawing.SolidBrush]::new($colors.Paper)
    $captionBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 163, 169, 178))
    $titleFont = [System.Drawing.Font]::new("Segoe UI Semibold", [Math]::Max(18, $Height * 0.13), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $captionFont = [System.Drawing.Font]::new("Segoe UI", [Math]::Max(12, $Height * 0.07), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

    $textX = $iconX + $iconBitmap.Width + ($Width * 0.05)
    $titleY = [int]($Height * 0.34)
    $captionY = [int]($Height * 0.56)
    $graphics.DrawString("Shutdown Scheduler", $titleFont, $titleBrush, $textX, $titleY)
    $graphics.DrawString("Dark schedule utility", $captionFont, $captionBrush, $textX, $captionY)

    $titleBrush.Dispose()
    $captionBrush.Dispose()
    $titleFont.Dispose()
    $captionFont.Dispose()
    $iconGraphics.Dispose()
    $iconBitmap.Dispose()
    $cardPen.Dispose()
    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Save-SplashLogo {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Initialize-Graphics $graphics
    $graphics.Clear($colors.Panel)

    $iconSize = [Math]::Min($Height * 0.42, $Width * 0.2)
    $iconBitmap = [System.Drawing.Bitmap]::new([int]$iconSize, [int]$iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $iconGraphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
    Initialize-Graphics $iconGraphics
    $iconGraphics.Clear([System.Drawing.Color]::Transparent)
    Draw-SchedulerIcon $iconGraphics $iconBitmap.Width $false $true

    $iconX = [int](($Width - $iconBitmap.Width) / 2)
    $iconY = [int](($Height - $iconBitmap.Height) / 2)
    $graphics.DrawImage($iconBitmap, $iconX, $iconY, $iconBitmap.Width, $iconBitmap.Height)

    $iconGraphics.Dispose()
    $iconBitmap.Dispose()
    $graphics.Dispose()
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Save-IcoFile {
    param(
        [string]$Path,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Initialize-Graphics $graphics
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-SchedulerIcon $graphics $Size $true $false
    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $icon.Save($stream)
    $stream.Dispose()
    $icon.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Save-SquareIcon (Join-Path $assetsDir "Square44x44Logo.scale-200.png") 88 $false
Save-SquareIcon (Join-Path $assetsDir "Square150x150Logo.scale-200.png") 300 $false
Save-SquareIcon (Join-Path $assetsDir "StoreLogo.png") 50 $false
Save-SquareIcon (Join-Path $assetsDir "LockScreenLogo.scale-200.png") 48 $false
Save-SquareIcon (Join-Path $assetsDir "IconPreview.png") 1024 $false
Save-SquareIcon (Join-Path $assetsDir "Square44x44Logo.targetsize-24_altform-unplated.png") 24 $true
Save-WideLogo (Join-Path $assetsDir "Wide310x150Logo.scale-200.png") 620 300
Save-SplashLogo (Join-Path $assetsDir "SplashScreen.scale-200.png") 1240 600
Save-IcoFile (Join-Path $assetsDir "AppIcon.ico") 64
