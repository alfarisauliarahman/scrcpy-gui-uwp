[Setup]
AppId={{5EAE50AB-EEF5-46B9-B3DA-2ABF535EF9D1}
AppName=Scrcpy GUI UWP
AppVersion=1.0.0
AppPublisher=KB-kil0bit
DefaultDirName={localappdata}\Scrcpy GUI UWP
DefaultGroupName=Scrcpy GUI UWP
OutputDir=Output
OutputBaseFilename=ScrcpyGUI_UWP_Installer
Compression=lzma2
SolidCompression=yes
SetupIconFile=Assets\app_icon.ico
UninstallDisplayIcon={app}\ScrcpyGui.exe
PrivilegesRequired=lowest
DisableDirPage=no
UsePreviousAppDir=no

[Files]
Source: "bin\Release\net10.0-windows\win-x64\publish\ScrcpyGui.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Scrcpy GUI UWP"; Filename: "{app}\ScrcpyGui.exe"
Name: "{autodesktop}\Scrcpy GUI UWP"; Filename: "{app}\ScrcpyGui.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\ScrcpyGui.exe"; Description: "Launch Scrcpy GUI UWP"; Flags: nowait postinstall skipifsilent
