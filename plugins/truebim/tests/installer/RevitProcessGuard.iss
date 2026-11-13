; Exercises the exact production guard without installing or removing any files.
[Setup]
AppName=TrueBIM Process Guard Test
AppVersion=0.2.55
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

function InitializeSetup: Boolean;
var
  Failed: Boolean;
begin
  { Ownership matrix; no real foreign processes are opened or terminated. }
  if not IsCurrentUserRevitProcess('Revit.exe', 'S-1-5-21-1', 'S-1-5-21-1') then
    RaiseException('Own Revit must block.');
  if not IsCurrentUserRevitProcess('revitcoreconsole.EXE', 'S-1-5-21-1', 'S-1-5-21-1') then
    RaiseException('Own headless Revit must block.');
  if IsCurrentUserRevitProcess('Revit.exe', 'S-1-5-21-2', 'S-1-5-21-1') then
    RaiseException('Foreign Revit must not block.');
  if IsCurrentUserRevitProcess('RevitCoreConsole.exe', 'S-1-5-21-2', 'S-1-5-21-1') then
    RaiseException('Foreign headless Revit must not block.');
  if IsCurrentUserRevitProcess('Other.exe', '', 'S-1-5-21-1') then
    RaiseException('Unrelated process must not block.');
  Failed := False;
  try
    IsCurrentUserRevitProcess('Revit.exe', '', 'S-1-5-21-1');
  except
    Failed := True;
  end;
  if not Failed then RaiseException('Unknown owner must fail closed.');
  Log('Current-user process ownership checks passed.');
  Result := True;
end;
