# ISOE — Install Software on Enterprises

Cliente nativo y ligero para instalar software de **64 bits** en Windows, en cola y en silencio. No necesita WinGet, Python ni .NET moderno en el equipo destino: utiliza .NET Framework de Windows.

## Descargar y usar

1. Descarga **ISOE.exe** desde [Releases](https://github.com/jmoreno-hesperia/ISOE/releases/latest). No necesitas instalar ISOE ni copiar otros archivos.
2. Ejecútalo y acepta el aviso de administrador de Windows.
3. Busca programas en el catálogo, marca los que quieras e inicia la instalación. Confirma la aceptación de sus licencias.
4. ISOE descarga e instala cada programa antes de pasar al siguiente. Muestra progreso de descarga, instalación activa y avance de la cola.

Las descargas y registros se conservan en `C:\Temp\ISOE\<identificador de ejecución>`. La carpeta ISOE se restringe a administradores y SYSTEM. El botón **Detener cola** cancela una descarga o espera a que termine el instalador activo; no interrumpe instalaciones a medias. Si un programa falla, queda registrado y continúa el siguiente.

La descarga tiene porcentaje real cuando el servidor comunica su tamaño. Durante la instalación se muestra actividad, porque los instaladores no ofrecen una API común para conocer un porcentaje real. Los reinicios requeridos se indican al finalizar; los parámetros silenciosos proceden del manifiesto del fabricante en WinGet.

## Catálogo inicial

| Programa | ID WinGet | Política |
|---|---|---|
| Adobe Acrobat Reader | `Adobe.Acrobat.Reader.64-bit` | Instalador x64 |
| Java Runtime 8 | `Oracle.JavaRuntimeEnvironment` | Instalador x64; se aplican las condiciones de Oracle |
| 7-Zip | `7zip.7zip` | Instalador x64 |
| AnyDesk | `AnyDesk.AnyDesk` | Visible pero bloqueado si no existe instalador x64 compatible |

**Compatible con Windows x64 no significa que una aplicación sea de 64 bits.** ISOE nunca recurre a x86, ARM64 o arquitectura neutral. Algunos programas, como AnyDesk en el catálogo actual, no cumplen este requisito. Los programas con dependencias, instaladores solo por usuario, paquetes portables/ZIP/MSIX o parámetros silenciosos desconocidos se rechazan en esta primera versión.

## Añadir aplicaciones con un formulario

Abre [Añadir software](https://github.com/jmoreno-hesperia/ISOE/issues/new?template=add-app.yml), o pulsa ese botón en ISOE. Indica **ID de WinGet**, **Nombre** y, opcionalmente, **Fabricante**. Puedes encontrar el ID con `winget search <nombre>` en un equipo que tenga WinGet.

- Las solicitudes abiertas por el propietario o colaboradores con permiso de escritura se tramitan automáticamente.
- Las solicitudes de otras personas requieren que un mantenedor aplique la etiqueta **new-app**. Si la etiqueta no existe, créala en Issues → Labels. Esto impide que terceros añadan software directamente a los equipos de la empresa.
- La acción valida ID, duplicados, existencia del paquete, arquitectura, SHA-256 y parámetros silenciosos. Actualiza `apps.json` y `catalog.json`, hace commit y cierra el issue si todo es correcto.
- Si falla, el issue queda abierto con un enlace al registro de Actions. Corrige la solicitud y vuelve a aplicar `new-app` para reintentarlo.

No se insertan respuestas de issues en comandos de shell ni se almacenan tokens en ISOE.exe.

## Actualización automática de enlaces

`apps.json` contiene los programas aprobados. **Actualizar catálogo x64** se ejecuta cada día a las 05:17 UTC y también puede iniciarse desde Actions → Run workflow. Consulta los manifiestos públicos de [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs), selecciona la versión más alta publicada y guarda URL, versión, SHA-256 y argumentos silenciosos en `catalog.json`.

El cliente lee `catalog.json` desde este repositorio al abrirse o al pulsar **Actualizar catálogo**. Así recibe enlaces nuevos sin recompilar. Si el repositorio es privado, este cliente no podrá descargar el catálogo sin una solución adicional de autenticación; nunca incrustes un token personal en el ejecutable.

Los binarios se descargan de las direcciones publicadas por WinGet, normalmente del fabricante. **No se redistribuyen instaladores de terceros dentro de este repositorio.** Cada archivo se verifica mediante SHA-256 antes de ejecutarlo; una discrepancia lo bloquea. Si un fabricante cambia un archivo sin actualizar su manifiesto, se bloqueará hasta que el hash correcto se publique. La actualización depende de la disponibilidad y actualización del catálogo de WinGet; no rastrea páginas web ni documentos arbitrarios.

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

Las pruebas automatizadas no sustituyen una prueba de instalación en una máquina virtual Windows limpia para cada programa y versión aprobada.
