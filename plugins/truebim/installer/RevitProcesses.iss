{ Included inside [Code]. Shared with the payload-free installer test harness. }
#include "CurrentUserProcesses.iss"

var
  RevitCloseForm: TSetupForm;
  RevitCleared: Boolean;
  RevitProcessList: TNewMemo;
  RevitCloseButton, RevitRetryButton: TNewButton;
  RevitProcesses: TRevitProcesses;

function RevitProcessServices: Variant;
var
  Locator: Variant;
begin
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Result := Locator.ConnectServer('', 'root\CIMV2');
end;

function ReadRevitProcesses(var Processes: TRevitProcesses; var Error: String): Boolean;
var
  Services, Items, Item: Variant;
  Snapshot: TRevitProcesses;
  I, N: Integer;
begin
  Result := False;
  Error := '';
  SetArrayLength(Processes, 0);
  try
    ReadCurrentUserRevitSnapshot(Snapshot);
    if GetArrayLength(Snapshot) > 0 then Services := RevitProcessServices;
    for I := 0 to GetArrayLength(Snapshot) - 1 do
    begin
      Items := Services.ExecQuery('SELECT ProcessId, Name, CreationDate FROM Win32_Process WHERE ProcessId = ' + Snapshot[I].Id);
      if Items.Count = 0 then continue; { Exited after the snapshot. }
      Item := Items.ItemIndex(0);
      if CompareText(String(Item.Name), Snapshot[I].Name) <> 0 then continue;
      N := GetArrayLength(Processes);
      SetArrayLength(Processes, N + 1);
      Processes[N] := Snapshot[I];
      if not VarIsNull(Item.CreationDate) then
        Processes[N].Created := Item.CreationDate;
    end;
    Result := True;
  except
    Error := 'Не удалось проверить процессы Revit: ' + GetExceptionMessage;
    SetArrayLength(Processes, 0);
    Log(Error);
  end;
end;

function DescribeRevitProcesses(const Processes: TRevitProcesses): String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Processes) - 1 do
    Result := Result + Processes[I].Name + ' — PID ' + Processes[I].Id +
      ', сеанс ' + IntToStr(Processes[I].SessionId) + #13#10;
end;

function RefreshRevitProcesses: Boolean;
var
  Error: String;
begin
  Result := False;
  if not ReadRevitProcesses(RevitProcesses, Error) then
  begin
    RevitProcessList.Text := Error + #13#10#13#10 +
      'Нажмите «Проверить снова». Если ошибка повторяется, обратитесь к администратору.';
    RevitCloseButton.Enabled := False;
    exit;
  end;
  Result := GetArrayLength(RevitProcesses) = 0;
  RevitCloseButton.Enabled := not Result;
  RevitProcessList.Text := DescribeRevitProcesses(RevitProcesses);
end;

procedure ContinueAfterRevitClosed;
begin
  RevitCleared := True;
  RevitCloseForm.Close;
end;

procedure RetryRevitProcessesClick(Sender: TObject);
begin
  if RefreshRevitProcesses then
    ContinueAfterRevitClosed;
end;

procedure CloseRevitProcessesClick(Sender: TObject);
var
  Services, Item, Owner: Variant;
  Confirmed: TRevitProcesses;
  I, Attempt, Code: Integer;
  Errors: String;
begin
  { Refresh before confirmation. Never terminate a process that was not listed. }
  if RefreshRevitProcesses then
  begin
    ContinueAfterRevitClosed;
    exit;
  end;
  if not RevitCloseButton.Enabled then
    exit;
  Confirmed := RevitProcesses;
  if MsgBox('Завершить перечисленные процессы Revit вашей учётной записи и продолжить установку?' + #13#10#13#10 +
      DescribeRevitProcesses(Confirmed) + #13#10 +
      'Все операции в этих процессах, включая расчёты, печать и синхронизацию, будут прерваны.' + #13#10 +
      'НЕСОХРАНЁННЫЕ ИЗМЕНЕНИЯ МОДЕЛЕЙ БУДУТ ПОТЕРЯНЫ.' + #13#10#13#10 +
      'Сначала сохраните модели. Нажимая «Да», вы подтверждаете принудительное завершение.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    exit;

  RevitCloseButton.Enabled := False;
  RevitRetryButton.Enabled := False;
  Errors := '';
  try
    try
      Services := RevitProcessServices;
      for I := 0 to GetArrayLength(Confirmed) - 1 do
      begin
        try
          Item := Services.Get('Win32_Process.Handle="' + Confirmed[I].Id + '"');
          { Do not act on a reused PID or a process whose identity cannot be checked. }
          if (Confirmed[I].Created = '') or VarIsNull(Item.CreationDate) then
            RaiseException('Не удалось проверить время запуска процесса.');
          if (String(Item.CreationDate) <> Confirmed[I].Created) or
             (String(Item.Name) <> Confirmed[I].Name) then
            RaiseException('Список процессов изменился. Повторите проверку.');
          Owner := Item.ExecMethod_('GetOwnerSid');
          if Integer(Owner.ReturnValue) <> 0 then
            RaiseException('Не удалось подтвердить владельца процесса. Завершение отменено.');
          if VarIsNull(Owner.Sid) then
            RaiseException('Windows не вернула владельца процесса. Завершение отменено.');
          if String(Owner.Sid) <> Confirmed[I].OwnerSid then
            RaiseException('Владелец процесса изменился. Завершение отменено.');
          Code := Item.Terminate(0);
          Log('Terminate ' + Confirmed[I].Name + ' PID ' + Confirmed[I].Id + ': ' + IntToStr(Code));
          if Code <> 0 then
          begin
            if (Code = 2) or (Code = 3) then
              RaiseException('Нет прав на завершение. Закройте Revit из запустившей его учётной записи или обратитесь к администратору.')
            else
              RaiseException('Ошибка завершения, код ' + IntToStr(Code) + '.');
          end;
        except
          Errors := Errors + Confirmed[I].Name + ' (PID ' + Confirmed[I].Id + '): ' +
            GetExceptionMessage + #13#10;
        end;
      end;
      { Termination may finish asynchronously. Fresh queries also detect newly started Revit. }
      for Attempt := 1 to 20 do
      begin
        if RefreshRevitProcesses then
        begin
          ContinueAfterRevitClosed;
          exit;
        end;
        Sleep(250);
      end;
      if Errors = '' then
        Errors := 'Revit ещё работает или был запущен повторно. Повторите проверку.';
      RevitProcessList.Text := RevitProcessList.Text + #13#10 + Errors;
      Log(Errors);
    except
      Errors := 'Не удалось завершить Revit: ' + GetExceptionMessage;
      RefreshRevitProcesses;
      RevitProcessList.Text := RevitProcessList.Text + #13#10 + Errors;
      Log(Errors);
    end;
  finally
    RevitRetryButton.Enabled := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Processes: TRevitProcesses;
  Error: String;
  Info: TNewStaticText;
  CancelButton: TNewButton;
begin
  Result := '';
  if ReadRevitProcesses(Processes, Error) then
  begin
    if GetArrayLength(Processes) = 0 then
      exit;
    Result := 'Перед установкой закройте Revit вашей учётной записи:' + #13#10 + DescribeRevitProcesses(Processes);
  end
  else
    Result := Error;
  Log(Result);
  { Unattended updates must never force-close models or display an interactive prompt. }
  if WizardSilent then
    exit;

  RevitCleared := False;
  RevitCloseForm := CreateCustomForm(540, 310, True, True);
  try
    RevitCloseForm.Caption := 'TrueBIM — завершение работы Revit';
    Info := TNewStaticText.Create(RevitCloseForm);
    Info.Parent := RevitCloseForm;
    Info.SetBounds(ScaleX(12), ScaleY(12), ScaleX(516), ScaleY(74));
    Info.AutoSize := False;
    Info.WordWrap := True;
    Info.Caption := 'Показаны только процессы Revit вашей учётной записи, включая другие её сеансы.' + #13#10 +
      'Сохраните модели и закройте их либо используйте кнопку завершения. Несохранённые изменения будут потеряны.' + #13#10 +
      'Установка выполняется только для вас. Revit других пользователей не затрагивается.';

    RevitProcessList := TNewMemo.Create(RevitCloseForm);
    RevitProcessList.Parent := RevitCloseForm;
    RevitProcessList.SetBounds(ScaleX(12), ScaleY(94), ScaleX(516), ScaleY(158));
    RevitProcessList.ReadOnly := True;
    RevitProcessList.ScrollBars := ssVertical;

    RevitCloseButton := TNewButton.Create(RevitCloseForm);
    RevitCloseButton.Parent := RevitCloseForm;
    RevitCloseButton.SetBounds(ScaleX(12), ScaleY(270), ScaleX(232), ScaleY(28));
    RevitCloseButton.Caption := 'Завершить Revit и продолжить…';
    RevitCloseButton.OnClick := @CloseRevitProcessesClick;

    RevitRetryButton := TNewButton.Create(RevitCloseForm);
    RevitRetryButton.Parent := RevitCloseForm;
    RevitRetryButton.SetBounds(ScaleX(252), ScaleY(270), ScaleX(156), ScaleY(28));
    RevitRetryButton.Caption := 'Проверить снова';
    RevitRetryButton.Default := True;
    RevitRetryButton.OnClick := @RetryRevitProcessesClick;

    CancelButton := TNewButton.Create(RevitCloseForm);
    CancelButton.Parent := RevitCloseForm;
    CancelButton.SetBounds(ScaleX(416), ScaleY(270), ScaleX(112), ScaleY(28));
    CancelButton.Caption := 'Отмена';
    CancelButton.Cancel := True;
    CancelButton.ModalResult := mrCancel;
    RevitCloseForm.ActiveControl := RevitRetryButton;
    RevitCloseForm.FlipAndCenterIfNeeded(True, WizardForm, False);
    if RefreshRevitProcesses then
      Result := ''
    else
    begin
      RevitCloseForm.ShowModal;
      if RevitCleared then
        Result := '';
    end;
  finally
    RevitCloseForm.Free;
  end;
end;
