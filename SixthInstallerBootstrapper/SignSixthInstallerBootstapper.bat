REM Make sure to compile the code to get a fresh exe. If the exe is alreay signed then signing the already signed exe will give errorlevel
REM Make sure that "C:\Program Files (x86)\WiX Toolset v3.11\bin\" & "C:\Program Files (x86)\Windows Kits\10\App Certification Kit" are in System Environment Path Variable
REM Run the batch file as an administrator
REM change current script directory to the directory where the script is located
cd /d %~dp0

del SixthInstallerBootstrapperEngine.exe

del payloadinfo.wxs

echo "Extract Engine from Executable"
insignia.exe -ib SixthInstallerBootstrapper.exe -o SixthInstallerBootstrapperEngine.exe

echo "Sign Extracted Engine"
signtool.exe sign /f Server.pfx /fd sha256 /t http://timestamp.digicert.com /debug SixthInstallerBootstrapperEngine.exe

echo "Merge Engine back to Executable (Overwrite)"
insignia.exe -ab SixthInstallerBootstrapperEngine.exe SixthInstallerBootstrapper.exe -o SixthInstallerBootstrapper.exe

echo "Sign Executable (Which now have Engine which is signed)"
signtool sign /f Server.pfx /fd sha256 /t http://timestamp.digicert.com /debug SixthInstallerBootstrapper.exe

echo "Validate Executable"
signtool.exe verify /v /pa SixthInstallerBootstrapper.exe

echo "Extract payloadinfo to use in Exepackage"
heat.exe payload SixthInstallerBootstrapper.exe -out payloadinfo.wxs

pause

