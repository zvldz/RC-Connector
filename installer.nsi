; RC-Connector NSIS Installer Script
; Requires: publish-folder/ built first (dotnet publish without SingleFile)

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"

; --- General ---
Name "RC-Connector"
OutFile "RC-Connector-Setup.exe"
InstallDir "$PROGRAMFILES64\RC-Connector"
InstallDirRegKey HKLM "Software\RC-Connector" "InstallDir"
RequestExecutionLevel admin

; --- Version info ---
!define VERSION "0.2.3"
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

; --- Languages ---
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Ukrainian"

; --- Localized strings ---
; Startup page
LangString STARTUP_CHECKBOX ${LANG_ENGLISH} "Run RC-Connector at Windows startup"
LangString STARTUP_CHECKBOX ${LANG_UKRAINIAN} "Запускати RC-Connector з Windows"

LangString STARTUP_TIP ${LANG_ENGLISH} "Tip: pin the RC-Connector tray icon so it is always visible.$\nRight-click taskbar $\u2192 Taskbar settings $\u2192 Select which icons appear."
LangString STARTUP_TIP ${LANG_UKRAINIAN} "Порада: закрiпiть iконку RC-Connector на панелi завдань.$\nПКМ панель завдань $\u2192 Параметри панелi $\u2192 Виберiть iконки."

; Close running instance
LangString CLOSE_APP_TITLE ${LANG_ENGLISH} "RC-Connector is running"
LangString CLOSE_APP_TITLE ${LANG_UKRAINIAN} "RC-Connector запущено"

LangString CLOSE_APP_MSG ${LANG_ENGLISH} "RC-Connector is currently running.$\nClose it to continue installation?"
LangString CLOSE_APP_MSG ${LANG_UKRAINIAN} "RC-Connector зараз запущено.$\nЗакрити для продовження встановлення?"

; --- Variables ---
Var StartupCheckbox

; --- Functions ---
Function .onInit
    ; Auto-select language based on Windows locale
    !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

; --- Startup page ---
Function StartupPage
    nsDialogs::Create 1018
    Pop $0

    ${NSD_CreateCheckbox} 10 10 280 20 "$(STARTUP_CHECKBOX)"
    Pop $StartupCheckbox
    ${NSD_Check} $StartupCheckbox

    ${NSD_CreateLabel} 10 40 350 40 "$(STARTUP_TIP)"
    Pop $0

    nsDialogs::Show
FunctionEnd

Function StartupPageLeave
    ${NSD_GetState} $StartupCheckbox $0
    ${If} $0 == ${BST_CHECKED}
        WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "RC-Connector" "$\"$INSTDIR\RC-Connector.exe$\""
    ${EndIf}
FunctionEnd

; --- Close running instance ---
Function CloseRunningInstance
    ; Check if process is running
    nsExec::ExecToStack 'tasklist /FI "IMAGENAME eq RC-Connector.exe" /NH'
    Pop $0  ; exit code
    Pop $1  ; output
    ${If} $0 == 0
        ; Check if output contains the process name (not "no tasks")
        StrCpy $2 $1 15
        ${If} $1 != ""
            ; Search for "RC-Connector" in output
            Push $1
            Push "RC-Connector.exe"
            Call StrContains
            Pop $3
            ${If} $3 != ""
                ; Process is running — ask user
                MessageBox MB_YESNO|MB_ICONQUESTION "$(CLOSE_APP_MSG)" /SD IDYES IDYES +2
                    Abort
                nsExec::ExecToLog 'taskkill /f /im RC-Connector.exe'
                Sleep 1000
            ${EndIf}
        ${EndIf}
    ${EndIf}
FunctionEnd

; --- StrContains helper ---
; Usage: Push "haystack" / Push "needle" / Call StrContains / Pop $result
; Returns needle if found, empty string if not
Function StrContains
    Exch $R1 ; needle
    Exch
    Exch $R2 ; haystack
    Push $R3
    Push $R4
    Push $R5

    StrLen $R3 $R1
    StrLen $R4 $R2
    ${If} $R3 > $R4
        StrCpy $R1 ""
        Goto done
    ${EndIf}

    IntOp $R4 $R4 - $R3
    StrCpy $R5 0

    loop:
        ${If} $R5 > $R4
            StrCpy $R1 ""
            Goto done
        ${EndIf}
        StrCpy $R0 $R2 $R3 $R5
        ${If} $R0 == $R1
            Goto done
        ${EndIf}
        IntOp $R5 $R5 + 1
        Goto loop

    done:
    Pop $R5
    Pop $R4
    Pop $R3
    Pop $R2
    Exch $R1
FunctionEnd

; --- Install ---
Section "Install"
    ; Close running instance (with user prompt)
    Call CloseRunningInstance

    ; Clean old files (upgrade: remove stale DLLs from previous version)
    ${If} ${FileExists} "$INSTDIR\RC-Connector.exe"
        RMDir /r "$INSTDIR"
    ${EndIf}

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
