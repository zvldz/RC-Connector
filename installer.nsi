; RC-Connector NSIS Installer Script
; Requires: publish-folder/ built first (dotnet publish without SingleFile)

!include "MUI2.nsh"

; --- General ---
Name "RC-Connector"
OutFile "RC-Connector-Setup.exe"
InstallDir "$PROGRAMFILES\RC-Connector"
InstallDirRegKey HKLM "Software\RC-Connector" "InstallDir"
RequestExecutionLevel admin

; --- Version info ---
!define VERSION "0.2.1"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "RC-Connector"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "RC-Connector Installer"
VIAddVersionKey "LegalCopyright" "P Team"

; --- UI ---
!define MUI_ICON "Resources\app.ico"
!define MUI_UNICON "Resources\app.ico"
!define MUI_ABORTWARNING

; --- Pages ---
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
Page custom StartupPage StartupPageLeave

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; --- Variables ---
Var StartupCheckbox

; --- Startup page ---
Function StartupPage
    nsDialogs::Create 1018
    Pop $0

    ${NSD_CreateCheckbox} 10 10 280 20 "Run RC-Connector at Windows startup"
    Pop $StartupCheckbox
    ${NSD_Check} $StartupCheckbox

    ${NSD_CreateLabel} 10 40 350 40 "Tip: pin the RC-Connector tray icon so it is always visible.$\nRight-click taskbar $\u2192 Taskbar settings $\u2192 Select which icons appear."
    Pop $0

    nsDialogs::Show
FunctionEnd

Function StartupPageLeave
    ${NSD_GetState} $StartupCheckbox $0
    ${If} $0 == ${BST_CHECKED}
        WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "RC-Connector" "$\"$INSTDIR\RC-Connector.exe$\""
    ${EndIf}
FunctionEnd

; --- Install ---
Section "Install"
    ; Close running instance if any
    nsExec::ExecToLog 'taskkill /f /im RC-Connector.exe'
    Sleep 1000

    SetOutPath "$INSTDIR"

    ; Copy all files from publish-folder
    File /r "publish-folder\*.*"

    ; Create uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; Start menu shortcut
    CreateDirectory "$SMPROGRAMS\RC-Connector"
    CreateShortCut "$SMPROGRAMS\RC-Connector\RC-Connector.lnk" "$INSTDIR\RC-Connector.exe"
    CreateShortCut "$SMPROGRAMS\RC-Connector\Uninstall.lnk" "$INSTDIR\Uninstall.exe"

    ; Registry (for Add/Remove Programs)
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector" "DisplayName" "RC-Connector"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector" "Publisher" "P Team"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector" "DisplayIcon" "$INSTDIR\RC-Connector.exe"
    WriteRegStr HKLM "Software\RC-Connector" "InstallDir" "$INSTDIR"
SectionEnd

; --- Uninstall ---
Section "Uninstall"
    ; Remove files
    RMDir /r "$INSTDIR"

    ; Remove shortcuts
    RMDir /r "$SMPROGRAMS\RC-Connector"

    ; Remove startup entry
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "RC-Connector"

    ; Remove registry
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\RC-Connector"
    DeleteRegKey HKLM "Software\RC-Connector"
SectionEnd
