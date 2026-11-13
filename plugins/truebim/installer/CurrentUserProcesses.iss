{ WTS supplies process owner SIDs without opening other users' processes.
  The installer writes to one user profile, shared by every session of that SID. }
type
  TRevitProcess = record
    Id: String;
    Name: String;
    Created: String;
    SessionId: LongWord;
    OwnerSid: String;
  end;
  TRevitProcesses = array of TRevitProcess;
  TWtsProcessInfo = record
    SessionId: LongWord;
    ProcessId: LongWord;
    ProcessName: UINT_PTR;
    UserSid: UINT_PTR;
  end;

function WtsEnumerateProcesses(Server: THandle; Reserved, Version: LongWord;
  var Buffer: UINT_PTR; var Count: LongWord): BOOL;
  external 'WTSEnumerateProcessesW@wtsapi32.dll stdcall';
procedure WtsFreeMemory(Buffer: UINT_PTR);
  external 'WTSFreeMemory@wtsapi32.dll stdcall';
procedure CopyWtsProcessInfo(var Destination: TWtsProcessInfo; Source: UINT_PTR; Size: UINT_PTR);
  external 'RtlMoveMemory@kernel32.dll stdcall';
function CurrentProcessId: LongWord;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function NativeStringLength(Value: UINT_PTR): Integer;
  external 'lstrlenW@kernel32.dll stdcall';
function CopyNativeString(Buffer: String; Source: UINT_PTR; Count: Integer): UINT_PTR;
  external 'lstrcpynW@kernel32.dll stdcall';
function SidToString(Sid: UINT_PTR; var Text: UINT_PTR): BOOL;
  external 'ConvertSidToStringSidW@advapi32.dll stdcall';
function FreeLocalMemory(Buffer: UINT_PTR): UINT_PTR;
  external 'LocalFree@kernel32.dll stdcall';

function ReadNativeString(Value: UINT_PTR): String;
var
  Length: Integer;
begin
  Result := '';
  if Value = 0 then exit;
  Length := NativeStringLength(Value);
  SetLength(Result, Length);
  if Length > 0 then CopyNativeString(Result, Value, Length + 1);
end;

function ReadOwnerSid(Sid: UINT_PTR): String;
var
  Text: UINT_PTR;
begin
  Result := '';
  if Sid = 0 then exit;
  if not SidToString(Sid, Text) then
    RaiseException('Не удалось прочитать владельца процесса: ' + SysErrorMessage(DLLGetLastError));
  try
    Result := ReadNativeString(Text);
  finally
    FreeLocalMemory(Text);
  end;
end;

function IsRevitProcessName(Name: String): Boolean;
begin
  Result := (CompareText(Name, 'Revit.exe') = 0) or
    (CompareText(Name, 'RevitCoreConsole.exe') = 0);
end;

function IsCurrentUserRevitProcess(Name, OwnerSid, CurrentSid: String): Boolean;
begin
  if CurrentSid = '' then RaiseException('Не удалось определить текущего пользователя Windows.');
  Result := False;
  if not IsRevitProcessName(Name) then exit;
  if OwnerSid = '' then RaiseException('Не удалось определить владельца процесса ' + Name + '.');
  Result := CompareText(OwnerSid, CurrentSid) = 0;
end;

procedure ReadCurrentUserRevitSnapshot(var Processes: TRevitProcesses);
var
  Buffer: UINT_PTR;
  Count, I, OwnPid: LongWord;
  Item: TWtsProcessInfo;
  CurrentSid, Name, OwnerSid: String;
  N: Integer;
begin
  SetArrayLength(Processes, 0);
  if not WtsEnumerateProcesses(0, 0, 1, Buffer, Count) then
    RaiseException('Не удалось получить процессы пользователя: ' + SysErrorMessage(DLLGetLastError));
  try
    CurrentSid := '';
    OwnPid := CurrentProcessId;
    if Count = 0 then RaiseException('Windows вернула пустой список процессов.');
    for I := 0 to Count - 1 do
    begin
      CopyWtsProcessInfo(Item, Buffer + I * SizeOf(Item), SizeOf(Item));
      if Item.ProcessId = OwnPid then
      begin
        CurrentSid := ReadOwnerSid(Item.UserSid);
        break;
      end;
    end;
    if CurrentSid = '' then RaiseException('Не удалось определить текущего пользователя Windows.');
    for I := 0 to Count - 1 do
    begin
      CopyWtsProcessInfo(Item, Buffer + I * SizeOf(Item), SizeOf(Item));
      Name := ReadNativeString(Item.ProcessName);
      if not IsRevitProcessName(Name) then continue;
      OwnerSid := ReadOwnerSid(Item.UserSid);
      if not IsCurrentUserRevitProcess(Name, OwnerSid, CurrentSid) then continue;
      N := GetArrayLength(Processes);
      SetArrayLength(Processes, N + 1);
      Processes[N].Id := IntToStr(Item.ProcessId);
      Processes[N].Name := Name;
      Processes[N].SessionId := Item.SessionId;
      Processes[N].OwnerSid := OwnerSid;
    end;
  finally
    WtsFreeMemory(Buffer);
  end;
end;
