@ECHO OFF
REM This script is used to pack the Playnite extension in debug mode.
REM It requires the Toolbox.exe to be in the Playnite local app data directory.
REM Ensure the Playnite local app data directory is set correctly.
REM Usage: Run this script from the command line or double-click it in Windows Explorer.

REM Set the path to the Toolbox executable
SET TOOLBOX_PATH=C:\Users\Branden\AppData\Local\Playnite\Toolbox.exe

REM Set the path to the extension's debug build directory
SET EXTENSION_DEBUG_PATH=C:\Users\Branden\Source\Repos\playnite-f95zone\bin\Debug

REM Set the output directory for the packed extension
SET OUTPUT_DIRECTORY=C:\Users\Branden\Downloads

REM Check if the Toolbox executable exists
IF NOT EXIST "%TOOLBOX_PATH%" (
	ECHO Toolbox executable not found at %TOOLBOX_PATH%.
	ECHO Please ensure that Playnite is installed and the Toolbox is available.
	EXIT /B 1
)

REM Check if the extension debug path exists
IF NOT EXIST "%EXTENSION_DEBUG_PATH%" (
	ECHO Extension debug path not found at %EXTENSION_DEBUG_PATH%.
	ECHO Please ensure that the extension is built in debug mode.
	EXIT /B 1
)

REM Check if the output directory exists, create it if it doesn't
IF NOT EXIST "%OUTPUT_DIRECTORY%" (
	ECHO Output directory not found at %OUTPUT_DIRECTORY%.
	ECHO Creating the directory...
	MKDIR "%OUTPUT_DIRECTORY%"
)

REM Pack the extension using the Toolbox
ECHO Packing the Playnite extension in debug mode...
ECHO This may take a moment, please wait...
"%TOOLBOX_PATH%" pack "%EXTENSION_DEBUG_PATH%" "%OUTPUT_DIRECTORY%"
IF %ERRORLEVEL% NEQ 0 (
	ECHO Failed to pack the extension. Please check the output for errors.
	EXIT /B %ERRORLEVEL%
)

ECHO Extension packed successfully and saved to %OUTPUT_DIRECTORY%.
ECHO You can now install the packed extension in Playnite.
ECHO Press any key to exit...
PAUSE > NUL