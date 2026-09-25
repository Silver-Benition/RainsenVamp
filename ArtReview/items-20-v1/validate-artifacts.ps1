param([string]$ReviewRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing.Common,System.Drawing.Primitives,System.Private.Windows.GdiPlus,System.Private.Windows.Core -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class ItemImageInspection {
    /// <summary>只读检查图像的透明像素与有效内容边界，不修改原始素材。</summary>
    public static long[] Inspect(string path) {
        using (var bmp = new Bitmap(path)) {
            var data = bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride*bmp.Height];
                Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                long clear=0,partial=0,opaque=0,border=0;
                int minX=bmp.Width,minY=bmp.Height,maxX=-1,maxY=-1;
                // 按 alpha 统计真实透明度，并确认主体没有碰到画布边界。
                for(int y=0;y<bmp.Height;y++) for(int x=0;x<bmp.Width;x++) {
                    byte a=pixels[y*stride+x*4+3];
                    if(a==0)clear++; else if(a==255)opaque++; else partial++;
                    if(a>16){minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                    if(a>0&&(x==0||y==0||x==bmp.Width-1||y==bmp.Height-1))border++;
                }
                return new long[]{bmp.Width,bmp.Height,clear,partial,opaque,border,minX,minY,maxX,maxY};
            } finally {bmp.UnlockBits(data);}
        }
    }
}
'@
$reviewRecords = Get-Content -LiteralPath (Join-Path $ReviewRoot 'generation-record.json') -Raw | ConvertFrom-Json
$reviewValidation = foreach($row in $reviewRecords) {
    $pixelStats = [ItemImageInspection]::Inspect($row.destination)
    $originalHash = (Get-FileHash -LiteralPath $row.source -Algorithm SHA256).Hash
    $copyHash = (Get-FileHash -LiteralPath $row.destination -Algorithm SHA256).Hash
    [pscustomobject]@{
        number=$row.number;name=$row.name;file=$row.destination
        width=$pixelStats[0];height=$pixelStats[1]
        transparentPixels=$pixelStats[2];partialAlphaPixels=$pixelStats[3];opaquePixels=$pixelStats[4]
        nontransparentBorderPixels=$pixelStats[5]
        contentBounds=@($pixelStats[6],$pixelStats[7],$pixelStats[8],$pixelStats[9])
        hasRealTransparency=($pixelStats[2] -gt 0 -and ($pixelStats[3]+$pixelStats[4]) -gt 0)
        sourceSha256=$originalHash;copySha256=$copyHash;copyVerified=($originalHash -eq $copyHash)
    }
}
$reviewResult = [pscustomobject]@{
    checkedAt=(Get-Date).ToString('o')
    expected=20;delivered=@($reviewValidation).Count
    allPresent=(@($reviewValidation).Count -eq 20)
    allCopiesVerified=(@($reviewValidation | Where-Object { -not $_.copyVerified }).Count -eq 0)
    allTransparent=(@($reviewValidation | Where-Object { -not $_.hasRealTransparency }).Count -eq 0)
    unityImported=$false;unityRuntimeTested=$false;native48PixelGridVerified=$false
    images=@($reviewValidation)
}
$reviewResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReviewRoot 'validation.json') -Encoding utf8
$reviewResult | Select-Object expected,delivered,allPresent,allCopiesVerified,allTransparent


