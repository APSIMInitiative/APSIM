
; Inno Setup Compiler 6.0.4

;APSIM setup script

#include  "ISPPBuiltins.iss"
#define ApsimX "..\..\.."
#define Setup ApsimX + "\Setup"

; We allow VERSION to be passed as a command-line parameter (e.g. iscc /DVERSION=1.2.3.4 apsimx.iss)
#ifdef VERSION
#define AppVerNo VERSION
#else
#define AppVerNo GetStringFileInfo(ApsimX + "\bin\Release\net8.0\win-x64\publish\Models.exe", PRODUCT_VERSION) 
#endif
#define GtkVer "3.24.24"
#define GtkArchive "gtk-" + GtkVer + ".zip"
#define GtkInstallPath "{localappdata}\Gtk\" + GtkVer

[Setup]
AppName=APSIM
AppVerName=APSIM v{#AppVerNo}
AppPublisherURL=https://www.apsim.info
ArchitecturesInstallIn64BitMode=x64
OutputBaseFilename=APSIMSetup
VersionInfoVersion={#AppVerNo}
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
;LicenseFile={#ApsimX}\license.txt
AppVersion={#AppVerNo}
AppID=APSIM{#AppVerNo}
DefaultDirName={autopf}\APSIM{#AppVerNo}
DefaultGroupName=APSIM{#AppVerNo}
UninstallDisplayIcon={app}\bin\ApsimNG.exe
Compression=lzma/Max
ChangesAssociations=true
WizardSmallImageFile={#Setup}\apsim_logo32.bmp
WizardImageFile={#Setup}\APSIMInitiativeBanner.bmp
;InfoBeforeFile=
VersionInfoCompany=APSIM Initiative4
VersionInfoDescription=Apsim Modelling
VersionInfoProductName=Apsim
VersionInfoProductVersion={#AppVerNo}
UsedUserAreasWarning=no

[Code]
var
    DownloadPage: TDownloadWizardPage;

function NeedToInstallGtk() : Boolean;
begin
    // Need to install gtk iff the appropriate directory doesn't exist.
    result := not DirExists(ExpandConstant('{#GtkInstallPath}'));
end;
function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
    if Progress = ProgressMax then
        Log(Format('Successfully downloaded file to {tmp}: %s', [FileName]));
    Result:= True;
end;

procedure InitializeWizard;
begin
    DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

// https://stackoverflow.com/questions/6065364/how-to-get-inno-setup-to-unzip-a-file-it-installed-all-as-part-of-the-one-insta
procedure UnZip(zipPath, targetPath: string);
var
    shell: Variant;
    zipFile: Variant;
    targetFolder: Variant;
    child: Variant;
    numItems, i: Integer;
begin
    shell := CreateOleObject('Shell.Application');
    zipFile := shell.NameSpace(zipPath);
    if VarIsClear(zipFile) then
        RaiseException(Format('Zip file "%s" does not exist or cannot be opened', [zipPath]));
    targetFolder := shell.NameSpace(targetPath);
    if VarIsClear(targetFolder) then
        RaiseException(Format('Target path "%s" does not exist', [targetPath]));

    numItems := zipFile.Items().Count;
    for i := 0 to numItems - 1 do
    begin
        child := zipFile.Items().Item(i);
        DownloadPage.SetText('Installing Gtk+ runtime...', Format('Extracting %s...', [child.Name]));
        DownloadPage.SetProgress(i, numItems);
        // The flags passed into this function are described here:
        // https://docs.microsoft.com/en-us/windows/win32/shell/folder-copyhere
        // 4 - Do not display a progress dialog box.
        // 16 - Respond with "Yes to All" for any dialog box that is displayed.
        targetFolder.CopyHere(child, {4 or} 16);
        if DownloadPage.AbortedByUser then
            break;
    end;
end;

function InstallGtk(): Boolean;
begin
    DownloadPage.AbortButton.Caption := 'Cancel';
    DownloadPage.Clear;
    DownloadPage.Add(ExpandConstant('https://github.com/GtkSharp/Dependencies/raw/master/{#GtkArchive}'), ExpandConstant('{#GtkArchive}'), '');
    DownloadPage.Show;
    try
        try
            DownloadPage.Download();
            ForceDirectories(ExpandConstant('{#GtkInstallPath}'));
            UnZip(ExpandConstant('{tmp}\{#GtkArchive}'), ExpandConstant('{#GtkInstallPath}'));
            DeleteFile('{tmp}\{#GtkArchive}');
            Result:= True;
        except
            if DownloadPage.AbortedByUser then
            begin
                Log('Download aborted by user.');
                SuppressibleMsgBox('Download aborted by user.', mbCriticalError, MB_OK, IDOK);
            end
            else
                SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
            DelTree(ExpandConstant('{#GtkInstallPath}'), True, True, True);
            Result:= False;
        end;
    finally
        DownloadPage.Hide;
    end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
    doInstall: Integer;
begin
    if CurPageID = wpSelectDir then
    begin
        if NeedToInstallGtk() then
        begin
            Log(ExpandConstant('Gtk directory {#GtkInstallPath} does not exist. Need to ask user if they want to install it...'));
            doInstall := MsgBox('Setup has detected that the required Gtk runtime is not installed. Do you wish to have it automatically downloaded (~50MB) and installed? The user interface will not run if this is not installed.', mbConfirmation, MB_YESNO);
            if doInstall = IDYES then
            begin
                Log('User chose to have gtk automatically installed. Performing installation now...');
                Result := InstallGtk()
            end
            else
            begin
                Log('User chose not to have gtk automatically installed.');
                // Continue installation if user doesn't want to install gtk.
                Result := True;
            end;
        end
        else
        begin
            Log(ExpandConstant('Gtk is already installed at {#GtkInstallPath}'));
            Result:= True;
        end;
    end
    else
        Result := True;
end;

// https://stackoverflow.com/questions/37825650/compare-version-strings-in-inno-setup
// 
// Returns 0, if the versions are equal.
// Returns -1, if the V1 is older than the V2.
// Returns 1, if the V1 is newer than the V2.
// 1.12 is considered newer than 1.1.
// 1.1 is considered the same as 1.1.0.
// Throws an exception, when a version is syntactically invalid (only digits and dots are allowed).
function CompareVersion(V1, V2: string): Integer;
var
  P, N1, N2: Integer;
begin
  Result := 0;
  while (Result = 0) and ((V1 <> '') or (V2 <> '')) do
  begin
    P := Pos('.', V1);
    if P > 0 then
    begin
      N1 := StrToInt(Copy(V1, 1, P - 1));
      Delete(V1, 1, P);
    end
      else
    if V1 <> '' then
    begin
      N1 := StrToInt(V1);
      V1 := '';
    end
      else
    begin
      N1 := 0;
    end;

    P := Pos('.', V2);
    if P > 0 then
    begin
      N2 := StrToInt(Copy(V2, 1, P - 1));
      Delete(V2, 1, P);
    end
      else
    if V2 <> '' then
    begin
      N2 := StrToInt(V2);
      V2 := '';
    end
      else
    begin
      N2 := 0;
    end;

    if N1 < N2 then Result := -1
      else
    if N1 > N2 then Result := 1;
  end;
end;

function HasDotNetCore(version: string) : boolean;
var
	runtimes: TArrayOfString;
	I: Integer;
	versionCompare: Integer;
	registryKey: string;
begin
	registryKey := 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App';
	if(not IsWin64) then
	   registryKey :=  'SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.NETCore.App';
	   
	Log('[.NET] Look for version ' + version);
	   
	if not RegGetValueNames(HKLM, registryKey, runtimes) then
	begin
	  Log('[.NET] Issue getting runtimes from registry');
	  Result := False;
	  Exit;
	end;
	
    for I := 0 to GetArrayLength(runtimes)-1 do
	begin
	  versionCompare := CompareVersion(runtimes[I], version);
	  Log(Format('[.NET] Compare: %s/%s = %d', [runtimes[I], version, versionCompare]));
	  if(not (versionCompare = -1)) then
	  begin
	    Log(Format('[.NET] Selecting %s', [runtimes[I]]));
	    Result := True;
	  	Exit;
	  end;
    end;
	Log('[.NET] No compatible found');
	Result := False;
end;

{ Read from the registry the path to the uninstaller }
{ @param version: the version to upgrade from }
function GetUninstallString(version: String): String;
var
  regKey: String;
  uninstaller: String;
begin
  regKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\APSIM' + version + '_is1';
  uninstaller := '';
  if not RegQueryStringValue(HKLM, regKey, 'UninstallString', uninstaller) then
    RegQueryStringValue(HKCU, regKey, 'UninstallString', uninstaller);
  Result := uninstaller;
end;

function UnInstallOldVersion(oldVersion : String): Integer;
var
  uninstaller: String;
  uninstallResult: Integer;
begin
{ Return Values: }
{ 1 - uninstall string is empty }
{ 2 - error executing the UnInstallString }
{ 3 - successfully executed the UnInstallString }

  { default return value }
  Result := 0;

  { get the uninstall string of the old app }
  uninstaller := GetUninstallString(oldVersion);
  if uninstaller <> '' then begin
    uninstaller := RemoveQuotes(uninstaller);
    if Exec(uninstaller, '/SILENT /NORESTART /SUPPRESSMSGBOXES','', SW_HIDE, ewWaitUntilTerminated, uninstallResult) then
      Result := 3
    else
      Result := 2;
  end else
    Result := 1;
end;

{ This function is called during the setup's initialisation. We check if the 'upgradefrom'
  command-line argument was provided, and if so, attempt to uninstall the previous version
  before installing this version. If the uninstallation fails, we ask the user if they wish
  to continue. The return value of this function is true iff installation should continue. }
function UpgradeIfNecessary(): Boolean;
var oldVersion : String;
var uninstallResult, continueInstall : Integer;
begin
  oldVersion := ExpandConstant('{param:upgradefrom|}')
  if (oldVersion = '') then
    Result := true
  else
  begin
    uninstallResult := UnInstallOldVersion(oldVersion);
    if (uninstallResult <> 3) then
    begin
      continueInstall := MsgBox('Uninstallation of previous version of APSIM was unsuccessful. Do you wish to continue installing the new version?', mbConfirmation, MB_YESNO);
      Result := continueInstall = IDYES;
    end
    else
      Result := True;
  end;
end;

// this is the main function that detects the required version
function IsRequiredDotNetDetected(): Boolean;  
begin
    result := HasDotNetCore('6.0.0');
end;

function InitializeSetup(): Boolean;
var
  answer: integer;
  ErrorCode: Integer;
begin
  result := true
  //check for the .net runtime. If it is not found then show a message.
  if not IsRequiredDotNetDetected() then 
  begin
      result := false;
      answer := MsgBox('The Microsoft .NET Core Runtime 6.0 or above is required.' + #13#10 + #13#10 +
      'Click OK to go to the web site or Cancel to quit', mbInformation, MB_OKCANCEL);
      if (answer = MROK) then
      begin
        ShellExecAsOriginalUser('open', 'https://download.visualstudio.microsoft.com/download/pr/4c5e26cf-2512-4518-9480-aac8679b0d08/523f1967fd98b0cf4f9501855d1aa063/windowsdesktop-runtime-6.0.13-win-x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
        result := true
      end;
  end;
  if result = true then
    result := UpgradeIfNecessary();
end;

[InstallDelete]
Name: {localappdata}\VirtualStore\Apsim\*.*; Type: filesandordirs
Name: {localappdata}\VirtualStore\Apsim; Type: dirifempty

[Files]
Source: {#ApsimX}\bin\Release\net8.0\win-x64\publish\*; DestDir: {app}\bin; Flags: ignoreversion;
;fixme
Source: {#ApsimX}\DeploymentSupport\global\*; DestDir: {app}\bin; Flags: ignoreversion;
Source: {#ApsimX}\DeploymentSupport\Windows\Bin64\sqlite3.dll; DestDir: {app}\bin; Flags: ignoreversion;
Source: {#ApsimX}\bin\Release\net8.0\win-x64\publish\Models.xml; DestDir: {app}\bin; Flags: ignoreversion; 
Source: {#ApsimX}\APSIM.Documentation\Resources\APSIM.bib; DestDir: {app}; Flags: ignoreversion;
Source: {#ApsimX}\ApsimNG\Resources\world\*; DestDir: {app}\ApsimNG\Resources\world; Flags: recursesubdirs
Source: {#ApsimX}\ApsimNG\Resources\CommonReportVariables\*; DestDir: {app}\ApsimNG\Resources\CommonReportVariables; Flags: recursesubdirs

;Sample files 
Source: {#ApsimX}\Examples\*; DestDir: {app}\Examples; Flags: recursesubdirs
Source: {#ApsimX}\Examples\*; DestDir: {autodocs}\Apsim\Examples; Flags: recursesubdirs
Source: {#ApsimX}\Tests\UnderReview\*; DestDir: {app}\UnderReview; Flags: recursesubdirs skipifsourcedoesntexist
Source: {#ApsimX}\Tests\UnderReview\*; DestDir: {autodocs}\Apsim\UnderReview; Flags: recursesubdirs skipifsourcedoesntexist

[Tasks]
Name: desktopicon; Description: Create a &desktop icon; GroupDescription: Additional icons:; Flags: unchecked
Name: associate; Description: &Associate .apsimx with Apsim Next Generation; GroupDescription: Other tasks:

[UninstallDelete]
Type: files; Name: "{app}\apsim.url"

[INI]
Filename: "{app}\apsim.url"; Section: "InternetShortcut"; Key: "URL"; String: "https://apsimnextgeneration.netlify.app/" 

[Icons]
;Name: {autoprograms}\APSIM{#AppVerNo}; Filename: {app}\bin\ApsimNG.exe
Name: "{group}\APSIM User Interface"; Filename: {app}\bin\ApsimNG.exe
Name: "{group}\APSIM Next Generation home page"; Filename: "{app}\apsim.url";
Name: {autodesktop}\APSIM{#AppVerNo}; Filename: {app}\bin\ApsimNG.exe; Tasks: desktopicon

[Registry]
;do the associations
Root: HKA; Subkey: "Software\Classes\.apsimx"; ValueType: string; ValueName: ""; ValueData: APSIMXFile; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile"; ValueType: string; ValueName: ""; ValueData: APSIM Next Generation File; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: {app}\bin\ApsimNG.exe,0; Tasks: associate
Root: HKA; Subkey: "Software\Classes\APSIMXFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\bin\ApsimNG.exe"" ""%1"""; Tasks: associate

[Run]
Filename: {app}\bin\ApsimNG.exe; Description: Launch APSIM; Flags: postinstall nowait skipifsilent
