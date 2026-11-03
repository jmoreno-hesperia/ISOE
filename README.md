# ISOE — Install Software on Enterprises

Cliente nativo y ligero para instalar software de **64 bits** en Windows, en cola y en silencio. La única excepción autorizada es **AnyDesk x86**, compatible con Windows x64. No necesita WinGet, Python ni .NET moderno en el equipo destino: utiliza .NET Framework de Windows.

## Descargar y usar

1. Descarga **ISOE.exe** desde [Releases](https://github.com/jmoreno-hesperia/ISOE/releases/latest). No necesitas instalar ISOE ni copiar otros archivos.
2. Ejecútalo y acepta el aviso de administrador de Windows.
3. Busca programas en el catálogo, marca los que quieras e inicia la instalación. Confirma la aceptación de sus licencias.
4. ISOE descarga e instala cada programa antes de pasar al siguiente. Muestra progreso de descarga, instalación activa y avance de la cola.

Las descargas se guardan temporalmente en `C:\Temp\ISOE\<identificador de ejecución>` y se borran automáticamente al terminar, cancelar o fallar la cola, después de que finalice el proceso activo. Los registros se conservan aparte en `C:\ProgramData\ISOE\Logs\<identificador>.log`. Las carpetas se restringen a administradores y SYSTEM. El botón **Detener cola** cancela una descarga o espera a que termine el instalador activo; no interrumpe instalaciones a medias. Si un programa falla, queda registrado y continúa el siguiente.

La limpieza solo admite la carpeta propia de esa ejecución: no elimina `C:\Temp`, registros, archivos de otras ejecuciones ni herramientas instaladas como QuickSupport. Rechaza enlaces simbólicos y junctions antes de borrar. Si Windows mantiene un archivo bloqueado, reintenta y muestra en pantalla y en el registro que quedan temporales pendientes.

Desde **v1.2.0**, la ventana es compacta (700 × 520 de área cliente), limita su tamaño al área útil de la pantalla, distribuye los botones en dos filas y adapta las columnas al ancho disponible. Incluye logo vectorial e icono, con la cabecera **Hesperia World** y **ISOE - Software para tu empresa**. El logo fuente está en `assets/logo.svg`; los recursos nativos y el icono se generan al compilar, sin dependencias adicionales.

La descarga tiene porcentaje real cuando el servidor comunica su tamaño. Durante la instalación se muestra actividad, porque los instaladores no ofrecen una API común para conocer un porcentaje real. Los reinicios requeridos se indican al finalizar; los parámetros silenciosos proceden del manifiesto del fabricante en WinGet.

## Catálogo inicial

| Programa | ID WinGet | Política |
|---|---|---|
| Adobe Acrobat Reader | `Adobe.Acrobat.Reader.64-bit` | Instalador x64 |
| Java Runtime 8 | `Oracle.JavaRuntimeEnvironment` | Instalador x64; se aplican las condiciones de Oracle |
| 7-Zip | `7zip.7zip` | Instalador x64 |
| AnyDesk | `AnyDesk.AnyDesk` | Ejecutable oficial x86; excepción autorizada explícitamente |
| Microsoft 365 (Office) | `Microsoft.Office` | Office x64 en español; elegir Business o Enterprise |
| TeamViewer QuickSupport | `TeamViewer.TeamViewer.QuickSupport` | Paquete portable oficial x64; se prepara sin abrirlo |

**Compatible con Windows x64 no significa que una aplicación sea de 64 bits.** ISOE rechaza x86, ARM64 y arquitectura neutral salvo la excepción concreta de AnyDesk (`allowX86: true`), visible en la interfaz. Ese permiso no puede aplicarse a otros programas. Los programas con dependencias, instaladores solo por usuario, ZIP/MSIX o parámetros silenciosos desconocidos siguen rechazándose.

## Microsoft 365 y QuickSupport

Se necesita **ISOE v1.1.0 o posterior** para estas incorporaciones. Las versiones anteriores conservan un catálogo compatible que indica que deben actualizarse.

**Microsoft 365:** al seleccionar Office, ISOE permite elegir Microsoft 365 Business u Office Apps for enterprise (E3/E5). El producto debe corresponder a vuestra licencia. Descarga el Office Deployment Tool de Microsoft, verifica su SHA-256 y su firma digital, extrae y verifica `setup.exe` y ejecuta `/download` y `/configure` con `OfficeClientEdition="64"`, idioma español y `Display Level="None"`. Los archivos de Office se descargan en `C:\Temp\ISOE\<ejecución>\Office365`. ODT es un extractor de 32 bits que despliega **Office de 64 bits**; su arquitectura no es la del Office instalado. La versión mostrada corresponde a ODT; Office se obtiene del canal Current.

La configuración no elimina Office existente, no migra su arquitectura ni fuerza el cierre de documentos. Cierra las aplicaciones de Office antes de comenzar. Si ya hay Office de 32 bits, la instalación puede fallar por incompatibilidad; ISOE no lo desinstala automáticamente. La activación se realiza después con una cuenta con licencia. Las fases de descarga e instalación de ODT muestran actividad, sin porcentaje inventado. Detener la cola espera a que termine la fase activa de ODT.

**TeamViewer QuickSupport:** no es un instalador convencional. ISOE descarga el paquete oficial x64, verifica SHA-256 y firma de TeamViewer, lo copia a `%ProgramFiles%\ISOE\Tools\TeamViewer.TeamViewer.QuickSupport\TeamViewerQS.exe` y crea un acceso directo común en el escritorio. No lo ejecuta ni inicia sesiones de soporte. La distribución x64 del fabricante puede utilizar un extractor x86 para el contenido x64. El uso atendido y la aceptación de avisos de TeamViewer se realizan al abrirlo manualmente.

**AnyDesk:** se instala silenciosamente desde su enlace oficial, comprobando SHA-256 y la firma de AnyDesk. Se identifica como x86 en la interfaz. No se configuran contraseñas de acceso desatendido ni conexiones remotas.

## Añadir aplicaciones con un formulario

Abre [Añadir software](https://github.com/jmoreno-hesperia/ISOE/issues/new?template=add-app.yml), o pulsa ese botón en ISOE. Indica **ID de WinGet**, **Nombre** y, opcionalmente, **Fabricante**. Puedes encontrar el ID con `winget search <nombre>` en un equipo que tenga WinGet.

- Las solicitudes abiertas por el propietario o colaboradores con permiso de escritura se tramitan automáticamente.
- Las solicitudes de otras personas requieren que un mantenedor aplique la etiqueta **new-app**. La acción **Preparar formularios de ISOE** crea esta etiqueta automáticamente. Esto impide que terceros añadan software directamente a los equipos de la empresa.
- La acción valida ID, duplicados, existencia del paquete, arquitectura, SHA-256 y parámetros silenciosos. Actualiza `apps.json` y `catalog.json`, hace commit y cierra el issue si todo es correcto.
- Si falla, el issue queda abierto con un enlace al registro de Actions. Corrige la solicitud y vuelve a aplicar `new-app` para reintentarlo.

No se insertan respuestas de issues en comandos de shell ni se almacenan tokens en ISOE.exe.

## Actualización automática de enlaces

`apps.json` contiene los programas aprobados. **Actualizar catálogo x64** se ejecuta cada día a las 05:17 UTC y también puede iniciarse desde Actions → Run workflow. Consulta los manifiestos públicos de [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), selecciona la versión más alta publicada y guarda URL, versión, SHA-256 y argumentos silenciosos en `catalog-v2.json`. También genera `catalog.json` para clientes antiguos.

El cliente lee `catalog-v2.json` desde este repositorio al abrirse o al pulsar **Actualizar catálogo**. Así recibe enlaces nuevos sin recompilar. Si el repositorio es privado, este cliente no podrá descargar el catálogo sin una solución adicional de autenticación; nunca incrustes un token personal en el ejecutable.

Si GitHub no responde, el cliente conserva el catálogo ya cargado o utiliza el incluido dentro del ejecutable. Muestra su fecha y avisa de que no ha podido actualizarlo. Las descargas siguen requiriendo Internet y un SHA-256 coincidente. Esto permite utilizar también la primera compilación antes de publicar el repositorio.

Los binarios se descargan de las direcciones publicadas por WinGet, normalmente del fabricante. **No se redistribuyen instaladores de terceros dentro de este repositorio.** Cada descarga directa se verifica mediante SHA-256 antes de ejecutarla; una discrepancia la bloquea. Si un fabricante cambia un archivo sin actualizar su manifiesto, se bloqueará hasta que el hash correcto se publique. QuickSupport tiene una URL oficial mutable: la acción descarga su archivo vigente diariamente y publica su hash actualizado; el cliente verifica además la firma del fabricante. En Office se valida el paquete ODT y la firma de setup.exe; ODT gestiona los archivos de Office desde Microsoft. No se rastrean documentos arbitrarios.

## Compilación y publicación

En PowerShell, desde el repositorio:

```powershell
./scripts/build.ps1
```

Se genera `dist\ISOE.exe` y su suma SHA-256. Usa el compilador de .NET Framework incluido en Windows, sin descargar SDKs ni dependencias de ejecución.

El workflow **Compilar ISOE para Windows** compila en cada cambio del cliente y publica un artefacto descargable. Un tag `v*` genera además una Release con ISOE.exe y SHA256SUMS.txt:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

El ejecutable no lleva certificado de firma de código; Windows puede mostrar el editor como desconocido. Para distribución empresarial con confianza del editor, firma la versión con el certificado de tu organización. Los equipos necesitan Windows 10/11 x64, conexión a GitHub y a los servidores de cada fabricante, permisos de administrador y .NET Framework 4.8.

## Desarrollo y verificación

Con Python 3.12:

```powershell
pip install -r scripts/requirements.txt
python -m unittest discover -s tests -v
python scripts/catalog.py
```

Las pruebas cubren rechazo de x86/ARM/neutral, HTTP, hashes inválidos, dependencias y parámetros desconocidos; herencia de argumentos; orden de versiones y lectura segura de formularios. `ISOE.exe --self-test` valida las restricciones del cliente sin instalar programas. `tests/ClientTests.cs` permite además renderizar la interfaz con un catálogo de prueba sin descargar ni instalar software.

El ejecutable de pruebas `ClientTests.exe --download-test` realiza opcionalmente una descarga real de 7-Zip mediante el código del cliente, verifica su hash y no ejecuta el instalador.

Las pruebas automatizadas no sustituyen una prueba de instalación en una máquina virtual Windows limpia para cada programa y versión aprobada.
