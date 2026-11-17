# Builds the signed Android app bundle (.aab) to upload to Google Play.
#
#   powershell -ExecutionPolicy Bypass -File build-release.ps1
#
# The first time, it creates your upload key (keytool asks for a password and a few details about you). Keep the
# .keystore file and its password safe and backed up: every update to the app must be signed with the same key.
# The password is typed here, never stored, and only passed to the build for the length of this script.
#
# Before each new upload, raise ApplicationVersion in StoryBoarder.Android/StoryBoarder.Android.csproj.
param(
    [string]$KeyStore = (Join-Path $env:USERPROFILE 'Documents\StoryBoarderKeys\storyboarder-upload.keystore')
)
$ErrorActionPreference = 'Stop'
$jdk = 'C:\Program Files\Android\Android Studio\jbr\bin'

if (-not (Test-Path $KeyStore)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $KeyStore) | Out-Null
    Write-Host "`nCreating your upload key: $KeyStore" -ForegroundColor Cyan
    Write-Host 'Choose a strong password and save it in your password manager. For "first and last name" you can give'
    Write-Host 'your name or Black Flame Studios; nobody sees these details except Google.' -ForegroundColor Gray
    & "$jdk\keytool.exe" -genkeypair -v -keystore $KeyStore -alias upload -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE -ne 0) { throw 'keytool did not create the key.' }
    Write-Host "`nBack up this file now (cloud drive or USB stick): $KeyStore`n" -ForegroundColor Yellow
}

$secure = Read-Host 'Upload key password' -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $env:STORYBOARDER_KEY_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    & "$jdk\keytool.exe" -list -keystore $KeyStore -alias upload -storepass:env STORYBOARDER_KEY_PASS | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'That password does not open the upload key.' }

    Write-Host "`nBuilding the signed app bundle (a couple of minutes)..." -ForegroundColor Cyan
    dotnet publish (Join-Path $PSScriptRoot 'StoryBoarder.Android') -c Release -nologo -v q `
        -p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$KeyStore" -p:AndroidSigningKeyAlias=upload `
        -p:AndroidSigningKeyPass=env:STORYBOARDER_KEY_PASS -p:AndroidSigningStorePass=env:STORYBOARDER_KEY_PASS
    if ($LASTEXITCODE -ne 0) { throw 'The build failed (see above).' }
}
finally {
    Remove-Item Env:STORYBOARDER_KEY_PASS -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

$aab = Join-Path $PSScriptRoot 'StoryBoarder.Android\bin\Release\net10.0-android\publish\com.blackflamestudios.storyboarder-Signed.aab'
$check = & "$jdk\jarsigner.exe" -verify $aab 2>&1 | Out-String
if ($check -notmatch 'jar verified') { throw "The bundle isn't signed properly: $aab`n$check" }
Write-Host "`nReady to upload to Google Play:" -ForegroundColor Green
Write-Host "  $aab"
Start-Process explorer.exe "/select,`"$aab`""
