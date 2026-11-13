; Exercises the exact production guard without installing or removing any files.
[Setup]
AppName=TrueBIM Process Guard Test
AppVersion=0.2.56
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
#include "..\..\installer\InstallFileAccess.iss"

function InstallationFileError: String;
var
  Root: String;
begin
  Result := '';
  Root := ExpandConstant('{param:GUARDROOT|}');
  if Root = '' then exit;
  try
    CheckInstallFolder(Root);
  except
    Result := GetExceptionMessage;
  end;
end;

#include "..\..\installer\RevitProcesses.iss"

function InitializeSetup: Boolean;
var
  Failed: Boolean;
  Unknown: TRevitProcess;
begin
  { Ownership matrix; no real foreign processes are opened or terminated. }
  if not IsBlockingRevitProcess('Revit.exe', 'S-1-5-21-1', 'S-1-5-21-1', 3, 3) then
    RaiseException('Own Revit must block.');
  if not IsBlockingRevitProcess('revitcoreconsole.EXE', 'S-1-5-21-1', 'S-1-5-21-1', 7, 3) then
    RaiseException('Own headless Revit must block.');
  if not IsBlockingRevitProcess('Revit.exe', 'S-1-5-21-1', 'S-1-5-21-1', 7, 3) then
    RaiseException('Own Revit in another session must block.');
  if IsBlockingRevitProcess('Revit.exe', 'S-1-5-21-2', 'S-1-5-21-1', 3, 3) then
    RaiseException('Foreign Revit must not block.');
  if IsBlockingRevitProcess('RevitCoreConsole.exe', 'S-1-5-21-2', 'S-1-5-21-1', 7, 3) then
    RaiseException('Foreign headless Revit must not block.');
  if IsBlockingRevitProcess('Other.exe', '', 'S-1-5-21-1', 3, 3) then
    RaiseException('Unrelated process must not block.');
  if IsBlockingRevitProcess('Revit.exe', '', 'S-1-5-21-1', 7, 3) then
    RaiseException('Unknown owner in another session must not fail the snapshot.');
  if IsBlockingRevitProcess('RevitCoreConsole.exe', '', 'S-1-5-21-1', 7, 3) then
    RaiseException('Unknown headless owner in another session must not block.');
  if not IsBlockingRevitProcess('Revit.exe', '', 'S-1-5-21-1', 3, 3) then
    RaiseException('Unknown owner in current session must block.');
  if not IsBlockingRevitProcess('RevitCoreConsole.exe', '', 'S-1-5-21-1', 3, 3) then
    RaiseException('Unknown headless owner in current session must block.');
  Unknown.OwnerSid := '';
  Unknown.Created := '20261113010000.000000+420';
  if CanTerminateRevitProcess(Unknown) then RaiseException('Unknown owner must never be terminated.');
  Unknown.OwnerSid := 'S-1-5-21-1';
  Unknown.Created := '';
  if CanTerminateRevitProcess(Unknown) then RaiseException('Unknown start time must prevent termination.');
  Failed := False;
  try
    IsBlockingRevitProcess('Revit.exe', '', '', 3, 3);
  except
    Failed := True;
  end;
  if not Failed then RaiseException('Unknown current user must fail closed.');
  Log('Terminal-server ownership/session/termination checks passed.');
  Result := True;
end;
