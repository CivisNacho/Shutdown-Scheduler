Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "..\Assets"
$assetsDir = [System.IO.Path]::GetFullPath($assetsDir)

$colors = @{
    Panel = [System.Drawing.Color]::FromArgb(255, 15, 18, 25)
    PanelEdge = [System.Drawing.Color]::FromArgb(255, 44, 48, 60)
    PanelGlow = [System.Drawing.Color]::FromArgb(255, 29, 34, 46)
    AccentStart = [System.Drawing.Color]::FromArgb(255, 31, 94, 228)
    AccentEnd = [System.Drawing.Color]::FromArgb(255, 73, 205, 255)
    PaperTop = [System.Drawing.Color]::FromArgb(255, 214, 219, 229)
    PaperBottom = [System.Drawing.Color]::FromArgb(255, 173, 180, 193)
    Dash = [System.Drawing.Color]::FromArgb(255, 64, 70, 89)
    Shadow = [System.Drawing.Color]::FromArgb(150, 0, 0, 0)
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

    $stroke = [Math]::Max(3, $Size * 0.085)
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
        $cardBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0, 0),
            [System.Drawing.PointF]::new($Size, $Size),
            $colors.PanelGlow,
            $colors.Panel)
        $cardBorder = [System.Drawing.Pen]::new($colors.PanelEdge, [Math]::Max(1.5, $Size * 0.018))
        $Graphics.FillPath($cardBrush, $cardPath)
        $Graphics.DrawPath($cardBorder, $cardPath)
        $cardBrush.Dispose()
        $cardBorder.Dispose()
        $cardPath.Dispose()
    }

    $glyphPadding = if ($TransparentBackground) { $Size * 0.12 } else { $Size * 0.15 }
    $ringRect = New-RectF $glyphPadding ($glyphPadding + ($Size * 0.01)) ($Size - ($glyphPadding * 2.0)) ($Size - ($glyphPadding * 2.0))
    $accentBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($ringRect.X, $ringRect.Bottom),
        [System.Drawing.PointF]::new($ringRect.Right, $ringRect.Y),
        $colors.AccentStart,
        $colors.AccentEnd)
    $accentPen = [System.Drawing.Pen]::new($accentBrush, $stroke)
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paperBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new($center, $ringRect.Y),
        [System.Drawing.PointF]::new($center, $ringRect.Bottom),
        $colors.PaperTop,
        $colors.PaperBottom)
    $paperPen = [System.Drawing.Pen]::new($paperBrush, $stroke * 0.92)
    $paperPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $paperPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $Graphics.DrawArc($accentPen, $ringRect.X, $ringRect.Y, $ringRect.Width, $ringRect.Height, 128, 248)

    $arrowPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $arrowScale = $Size * 0.175
    $arrowBaseX = $ringRect.Right - ($Size * 0.02)
    $arrowBaseY = $ringRect.Y + ($Size * 0.15)
    $arrowPoints = @(
        [System.Drawing.PointF]::new($arrowBaseX - ($arrowScale * 1.18), $arrowBaseY + ($arrowScale * 0.1)),
        [System.Drawing.PointF]::new($arrowBaseX - ($arrowScale * 0.46), $arrowBaseY - ($arrowScale * 0.46)),
        [System.Drawing.PointF]::new($arrowBaseX, $arrowBaseY + ($arrowScale * 0.62))
    )
    $arrowPath.AddPolygon($arrowPoints)
    $Graphics.FillPath($accentBrush, $arrowPath)

    $powerRect = New-RectF ($center - ($Size * 0.16)) ($center - ($Size * 0.14)) ($Size * 0.32) ($Size * 0.32)
    $Graphics.DrawArc($paperPen, $powerRect.X, $powerRect.Y, $powerRect.Width, $powerRect.Height, 42, 276)

    $stemWidth = $stroke * 0.86
    $stemHeight = $Size * 0.21
    $stemX = $center - ($stemWidth / 2)
    $stemY = $powerRect.Y - ($Size * 0.005)
    $stemPath = New-RoundedRectPath $stemX $stemY $stemWidth $stemHeight ($stemWidth * 0.5)
    $Graphics.FillPath($paperBrush, $stemPath)

    if ($TransparentBackground) {
        $dashPen = [System.Drawing.Pen]::new($colors.Dash, [Math]::Max(2, $stroke * 0.44))
        $dashPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $dashPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $Graphics.DrawArc($dashPen, $ringRect.X, $ringRect.Y, $ringRect.Width, $ringRect.Height, 20, 62)
        $dashPen.Dispose()
    }
    elseif (-not $WideSurface) {
        $dashBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(230, $colors.Dash))
        $dashWidth = $stroke * 0.58
        $dashHeight = $stroke * 1.18
        $dashRadius = $dashWidth * 0.35
        $angles = @(16, 34, 52, 70)
        foreach ($angle in $angles) {
            $radians = [Math]::PI * $angle / 180.0
            $radius = ($ringRect.Width / 2.0) + ($stroke * 0.64)
            $dashCenterX = $center + ([Math]::Cos($radians) * $radius)
            $dashCenterY = $center + ([Math]::Sin($radians) * $radius)
            $dashPath = New-RoundedRectPath ($dashCenterX - ($dashWidth / 2)) ($dashCenterY - ($dashHeight / 2)) $dashWidth $dashHeight $dashRadius
            $matrix = [System.Drawing.Drawing2D.Matrix]::new()
            $matrix.RotateAt($angle + 4, [System.Drawing.PointF]::new($dashCenterX, $dashCenterY))
            $dashPath.Transform($matrix)
            $Graphics.FillPath($dashBrush, $dashPath)
            $matrix.Dispose()
            $dashPath.Dispose()
        }
        $dashBrush.Dispose()
    }

    $accentBrush.Dispose()
    $paperBrush.Dispose()
    $accentPen.Dispose()
    $paperPen.Dispose()
    $arrowPath.Dispose()
    $stemPath.Dispose()
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

    $titleBrush = [System.Drawing.SolidBrush]::new($colors.PaperTop)
    $captionBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(220, 163, 169, 178))
    $titleFont = [System.Drawing.Font]::new("Segoe UI Semibold", [Math]::Max(18, $Height * 0.13), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $captionFont = [System.Drawing.Font]::new("Segoe UI", [Math]::Max(12, $Height * 0.07), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)

    $textX = $iconX + $iconBitmap.Width + ($Width * 0.05)
    $titleY = [int]($Height * 0.34)
    $captionY = [int]($Height * 0.56)
    $graphics.DrawString("Shutdown Scheduler", $titleFont, $titleBrush, $textX, $titleY)
    $graphics.DrawString("Timed power control", $captionFont, $captionBrush, $textX, $captionY)

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
