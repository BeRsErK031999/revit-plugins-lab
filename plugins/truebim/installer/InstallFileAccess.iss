{ Read-only preflight: OPEN_EXISTING requests write/delete access without writing
  or truncating. This detects DLL image mappings and incompatible open handles
  even when Windows does not expose the owning process's SID/session. }
function OpenInstallFile(Path: String; Access, Share: LongWord; Security: UINT_PTR;
  Disposition, Flags: LongWord; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseInstallFile(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';
function InstallFileAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

procedure RaiseInstallFileError(Path: String; Code: LongWord);
begin
  RaiseException('Файл TrueBIM занят или недоступен:' + #13#10 + Path + #13#10 +
    SysErrorMessage(Code) + ' (код ' + IntToStr(Code) + ').' + #13#10 +
    'Закройте Revit во всех сеансах своей учётной записи и повторите проверку.');
end;

procedure CheckInstallFile(Path: String);
var
  Handle: THandle;
  Code: LongWord;
begin
  { GENERIC_WRITE | DELETE; share read/write/delete; OPEN_EXISTING. }
  Handle := OpenInstallFile(Path, $40010000, 7, 0, 3, $80, 0);
  if Handle = THandle(-1) then
  begin
    Code := DLLGetLastError;
    if (Code <> 2) and (Code <> 3) then RaiseInstallFileError(Path, Code);
  end
  else
    CloseInstallFile(Handle);
end;

procedure CheckInstallFolder(Path: String);
var
  Attributes, Code: LongWord;
  Found: TFindRec;
  Child: String;
begin
  Attributes := InstallFileAttributes(Path);
  if Attributes = $FFFFFFFF then
  begin
    Code := DLLGetLastError;
    if (Code <> 2) and (Code <> 3) then RaiseInstallFileError(Path, Code);
    exit;
  end;
  if not FindFirst(AddBackslash(Path) + '*', Found) then
  begin
    Code := DLLGetLastError;
    if (Code <> 2) and (Code <> 18) then RaiseInstallFileError(Path, Code);
    exit;
  end;
  try
    repeat
      if (Found.Name = '.') or (Found.Name = '..') then continue;
      Child := AddBackslash(Path) + Found.Name;
      if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        { Do not follow junctions recursively outside the installation tree. }
        if (Found.Attributes and $400) <> 0 then
          RaiseException('Не удалось проверить ссылку на папку TrueBIM: ' + Child);
        CheckInstallFolder(Child);
      end
      else
        CheckInstallFile(Child);
    until not FindNext(Found);
    Code := DLLGetLastError;
    if (Code <> 0) and (Code <> 18) then RaiseInstallFileError(Path, Code);
  finally
    FindClose(Found);
  end;
end;
