; Instalador del cliente de escritorio SITBIO (RNF-011, DT-013 del DOC-09).
;
; Empaqueta la publicación autocontenida del cliente WPF:
;   dotnet publish src/SITBIO.Desktop -c Release -r win-x64 --self-contained true -o publish/desktop
; y la compila con Inno Setup 6. Lo más sencillo es usar scripts/instalador.ps1, que hace los dos pasos.
;
; Parámetros de compilación (ISCC /D...):
;   Version   versión del instalador (por defecto la de Directory.Build.props, la pasa el script).
;   ApiUrl    dirección de la API que se propone en el asistente (SITBIO_API_URL).
;   Publicado carpeta con la publicación (por defecto ..\publish\desktop).
;
; Instalación silenciosa con otra API:
;   SITBIO-Setup-<versión>.exe /VERYSILENT /API=https://api.sitbio.co/api/v1/

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef ApiUrl
  #define ApiUrl "http://localhost:5080/api/v1/"
#endif
#ifndef Publicado
  #define Publicado "..\publish\desktop"
#endif

#define Nombre "SITBIO"
#define Ejecutable "SITBIO.exe"

[Setup]
; El AppId identifica la instalación para actualizarla y desinstalarla: no se cambia entre versiones.
AppId={{6F1B6C1E-2B7D-4C4B-9E3A-51B1D6A0C011}
AppName={#Nombre}
AppVersion={#Version}
AppVerName={#Nombre} {#Version}
AppPublisher=Equipo SITBIO - SENA CTMA
AppComments=Gestión del mantenimiento de equipos biomédicos
; Sin permisos de administrador: se instala para el usuario actual (%LOCALAPPDATA%\Programs\SITBIO).
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#Nombre}
DefaultGroupName={#Nombre}
DisableProgramGroupPage=yes
UninstallDisplayName={#Nombre}
UninstallDisplayIcon={app}\{#Ejecutable}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=SITBIO-Setup-{#Version}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Avisa a Windows de que cambió SITBIO_API_URL para que la vean las aplicaciones que se abran después.
ChangesEnvironment=yes
CloseApplications=yes

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "{#Publicado}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"
Name: "{group}\Desinstalar {#Nombre}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nombre}"; Filename: "{app}\{#Ejecutable}"; Tasks: escritorio

[Registry]
; Variable de usuario que lee ConfiguracionApi; se borra al desinstalar.
Root: HKCU; Subkey: "Environment"; ValueType: string; ValueName: "SITBIO_API_URL"; ValueData: "{code:UrlApi}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#Ejecutable}"; Description: "Abrir {#Nombre}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PaginaApi: TInputQueryWizardPage;

function UrlPropuesta(): String;
begin
  // Orden: parámetro /API=, valor de una instalación anterior, valor de compilación.
  Result := ExpandConstant('{param:API|}');
  if Result = '' then
    if not RegQueryStringValue(HKCU, 'Environment', 'SITBIO_API_URL', Result) then
      Result := '';
  if Result = '' then
    Result := '{#ApiUrl}';
end;

procedure InitializeWizard();
begin
  PaginaApi := CreateInputQueryPage(wpSelectTasks,
    'Conexión con el servidor',
    '¿A qué servidor de SITBIO se conecta este equipo?',
    'Escribe la dirección de la API que te dio el administrador de tu institución. ' +
    'Se guarda en la variable SITBIO_API_URL y se puede cambiar reinstalando.');
  PaginaApi.Add('Dirección de la API:', False);
  PaginaApi.Values[0] := UrlPropuesta();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Url: String;
begin
  Result := True;
  if CurPageID = PaginaApi.ID then
  begin
    Url := Trim(PaginaApi.Values[0]);
    if (Pos('http://', Lowercase(Url)) <> 1) and (Pos('https://', Lowercase(Url)) <> 1) then
    begin
      MsgBox('La dirección debe empezar por http:// o https://', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function UrlApi(Param: String): String;
begin
  Result := Trim(PaginaApi.Values[0]);
  if (Result <> '') and (Copy(Result, Length(Result), 1) <> '/') then
    Result := Result + '/';
end;
