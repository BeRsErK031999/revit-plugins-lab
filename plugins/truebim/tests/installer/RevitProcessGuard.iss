; Exercises the exact production guard without installing or removing any files.
[Setup]
AppName=TrueBIM Process Guard Test
AppVersion=0.2.54
DefaultDirName={tmp}\TrueBIM-Guard-Test
CreateAppDir=no
Uninstallable=no
UpdateUninstallLogAppName=no
PrivilegesRequired=lowest
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
OutputDir=..\..\..\..\dist\installer-tests
OutputBaseFilename=RevitProcessGuard-Test

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Code]
#include "..\..\installer\RevitProcesses.iss"
