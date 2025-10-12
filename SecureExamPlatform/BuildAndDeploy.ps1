# Secure Exam Platform - Build and Setup Script
# This PowerShell script builds and sets up the application using only free tools

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    Secure Exam Platform Builder      " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if .NET SDK is installed
Write-Host "Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "✓ .NET SDK Version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ .NET SDK not found. Please install .NET 7.0 SDK from https://dot.net" -ForegroundColor Red
    exit 1
}

# Create project structure
Write-Host ""
Write-Host "Creating project structure..." -ForegroundColor Yellow
$folders = @("Models", "Core", "Security", "UI", "bin", "obj")
foreach ($folder in $folders) {
    if (!(Test-Path $folder)) {
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Write-Host "✓ Created folder: $folder" -ForegroundColor Green
    }
}

# Build the application
Write-Host ""
Write-Host "Building application..." -ForegroundColor Yellow
try {
    dotnet build --configuration Release --verbosity minimal
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Build successful!" -ForegroundColor Green
    } else {
        Write-Host "✗ Build failed!" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Build error: $_" -ForegroundColor Red
    exit 1
}

# Create deployment package
Write-Host ""
Write-Host "Creating deployment package..." -ForegroundColor Yellow
$deployPath = "Deploy"
if (Test-Path $deployPath) {
    Remove-Item $deployPath -Recurse -Force
}
New-Item -ItemType Directory -Path $deployPath -Force | Out-Null

# Copy build output
Copy-Item "bin\Release\net7.0-windows\*" $deployPath -Recurse -Force
Write-Host "✓ Copied application files" -ForegroundColor Green

# Create startup script
$startupScript = @"
@echo off
echo Starting Secure Exam Platform...
echo.
echo WARNING: This application will lock your computer during exams.
echo Only proceed if you are authorized to take an exam.
echo.
pause
start SecureExamPlatform.exe
"@

$startupScript | Out-File -FilePath "$deployPath\StartExam.bat" -Encoding ASCII
Write-Host "✓ Created startup script" -ForegroundColor Green

# Create installation guide
$installGuide = @"
# Secure Exam Platform - Installation Guide

## System Requirements
- Windows 10 or later
- .NET 7.0 Runtime (will be installed automatically if missing)
- 2GB RAM minimum
- 500MB disk space

## Installation Steps

1. **Extract Files**: Extract all files to a folder (e.g., C:\SecureExam)

2. **Run as Regular User**: 
   - Double-click "StartExam.bat" to launch the application
   - NO administrator privileges required for student use

3. **Test Login**:
   - Student ID: TEST2025
   - Session Token: EXAM2025-ABC123-XYZ789

## Faculty Setup

To create custom exams, edit the mock exam data in the ExamSessionManager.cs file.

## Security Features Enabled

✓ Process monitoring and termination
✓ Screenshot prevention
✓ Keyboard shortcut blocking  
✓ Window management lockdown
✓ Clipboard monitoring
✓ Session management
✓ Automatic submission on time expiry

## Troubleshooting

**Issue**: Antivirus blocks the application
**Solution**: Add exception for SecureExamPlatform.exe in your antivirus

**Issue**: Application won't start
**Solution**: Install .NET 7.0 Runtime from https://dot.net

**Issue**: Screen goes black or flickers
**Solution**: Update graphics drivers

## Support

For technical support, contact your system administrator.
"@

$installGuide | Out-File -FilePath "$deployPath\README.md" -Encoding UTF8
Write-Host "✓ Created installation guide" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "           BUILD COMPLETE!              " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Deployment files created in: $deployPath" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Test the application: cd $deployPath && .\StartExam.bat" -ForegroundColor White
Write-Host "2. Use test credentials:"  -ForegroundColor White
Write-Host "   Student ID: TEST2025" -ForegroundColor Cyan
Write-Host "   Token: EXAM2025-ABC123-XYZ789" -ForegroundColor Cyan
Write-Host "3. Deploy to exam computers" -ForegroundColor White
Write-Host ""
Write-Host "⚠️  IMPORTANT: Test thoroughly before using in production!" -ForegroundColor Red