"""Resolve only unattended x64 installers from microsoft/winget-pkgs. No binaries in Git."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import urllib.request
import urllib.error
import yaml

ROOT = Path(__file__).resolve().parent.parent
ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]*(?:\.[A-Za-z0-9][A-Za-z0-9_-]*)+")
DEFAULTS = {
    "msi": "/qn /norestart", "wix": "/qn /norestart",
    "inno": "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
    "nullsoft": "/S", "burn": "/quiet /norestart",
}


def get(url):
    headers = {"User-Agent": "ISOE-catalog", "Accept": "application/vnd.github+json"}
    if os.environ.get("GITHUB_TOKEN") and url.startswith("https://api.github.com/"):
        headers["Authorization"] = "Bearer " + os.environ["GITHUB_TOKEN"]
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=45) as response:
        return response.read().decode("utf-8-sig")


def version_key(value):
    return [(1, int(x)) if x.isdigit() else (0, x.lower()) for x in re.findall(r"\d+|\D+", value)]


def validate_app(app):
    if not ID.fullmatch(app.get("id", "")):
        raise ValueError("ID de WinGet no válido")
    for key in ("name", "publisher"):
        if not isinstance(app.get(key, ""), str) or len(app.get(key, "")) > 120:
            raise ValueError("Campo no válido: " + key)
    if not app.get("name", "").strip():
        raise ValueError("El nombre es obligatorio")


def select_installer(manifest, architecture="x64", allow_portable=False):
    candidates = []
    for item in manifest.get("Installers", []):
        merged = {**manifest, **item}
        # User-only and dependency-bearing packages require extra orchestration.
        if merged.get("Architecture") != architecture or merged.get("Scope") == "user":
            continue
        if merged.get("Dependencies") or merged.get("InstallerType") not in (*DEFAULTS, "exe", *(('portable',) if allow_portable else ())):
            continue
        if merged.get("InstallModes") and "silent" not in merged["InstallModes"]:
            continue
        switches = {**manifest.get("InstallerSwitches", {}), **item.get("InstallerSwitches", {})}
        kind = merged["InstallerType"]
        silent = "" if kind == "portable" else switches.get("Silent") or DEFAULTS.get(kind)
        if not silent and kind != "portable":
            continue
        args = " ".join(filter(None, [silent, switches.get("Custom", "")]))
        # Placeholders that require WinGet substitution are not executable here.
        if "<" in args or ">" in args or "\n" in args or "\r" in args:
            continue
        url, sha = merged.get("InstallerUrl", ""), merged.get("InstallerSha256", "")
        if not url.startswith("https://") or not re.fullmatch(r"[a-fA-F0-9]{64}", sha):
            continue
        candidates.append({
            "architecture": architecture, "url": url, "sha256": sha.upper(),
            "type": kind, "arguments": args,
            "successCodes": sorted(set([0, 1641, 3010] + merged.get("InstallerSuccessCodes", []))),
            "locale": merged.get("InstallerLocale", ""),
            "productCodes": sorted({str(x) for x in [merged.get("ProductCode")] + [e.get("ProductCode") for e in merged.get("AppsAndFeaturesEntries", [])] if x}),
            "upgradeCodes": sorted({str(x) for x in [merged.get("UpgradeCode")] + [e.get("UpgradeCode") for e in merged.get("AppsAndFeaturesEntries", [])] if x}),
            "detectionNames": sorted({e["DisplayName"] for e in merged.get("AppsAndFeaturesEntries", []) if e.get("DisplayName")}),
            "detectionPublishers": sorted({e["Publisher"] for e in merged.get("AppsAndFeaturesEntries", []) if e.get("Publisher")}),
        })
    candidates.sort(key=lambda x: (x["locale"] not in ("es-ES", "es"), x["locale"] not in ("", "en-US")))
    if not candidates:
        raise ValueError("Sin instalador x64 silencioso compatible (o requiere dependencias/WinGet).")
    result = candidates[0]
    del result["locale"]
    return result


def get_manifest(package):
    path = "manifests/" + package[0].lower() + "/" + package.replace(".", "/")
    listing = json.loads(get("https://api.github.com/repos/microsoft/winget-pkgs/contents/" + path))
    versions = [x["name"] for x in listing if x["type"] == "dir"]
    if not versions:
        raise ValueError("Paquete sin versiones")
    # Some package directories also contain child IDs, e.g. Microsoft.Office/2010.
    # Skip only a missing manifest (not network/auth/server failures).
    for version in sorted(versions, key=version_key, reverse=True):
        base = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/" + path + "/" + version + "/" + package
        try:
            manifest = yaml.safe_load(get(base + ".installer.yaml"))
        except urllib.error.HTTPError as error:
            if error.code == 404:
                continue
            raise
        if manifest.get("PackageIdentifier") != package or str(manifest.get("PackageVersion")) != version:
            raise ValueError("Identidad de manifiesto incorrecta")
        return version, manifest
    raise ValueError("No existe manifiesto de instalación para este ID")


def official_quicksupport_hash(url):
    # The official URL is mutable and can be newer than the WinGet manifest.
    # Hash the current vendor binary so updates never need a client rebuild.
    if url != "https://download.teamviewer.com/download/TeamViewerQS_x64.exe":
        raise ValueError("URL de QuickSupport no autorizada")
    request = urllib.request.Request(url, headers={"User-Agent": "ISOE-catalog"})
    digest, size = hashlib.sha256(), 0
    with urllib.request.urlopen(request, timeout=90) as response:
        if not response.geturl().startswith("https://"):
            raise ValueError("Redirección sin HTTPS")
        while chunk := response.read(131072):
            size += len(chunk)
            if size > 200 * 1024 * 1024:
                raise ValueError("QuickSupport supera el tamaño esperado")
            digest.update(chunk)
    if size < 1024:
        raise ValueError("Descarga de QuickSupport vacía o no válida")
    return digest.hexdigest().upper()


def resolve(app):
    validate_app(app)
    package = app["id"]
    if app.get("allowX86") and package != "AnyDesk.AnyDesk":
        raise ValueError("La excepción x86 está limitada a AnyDesk")
    if package == "Microsoft.Office":
        version, manifest = get_manifest("Microsoft.OfficeDeploymentTool")
        item = next(x for x in manifest["Installers"] if x["Architecture"] == "x86")
        # ODT is an x86 extractor; OfficeClientEdition=64 determines Office's bits.
        return {**app, "available": True, "version": "ODT " + version,
                "architecture": "x64", "bootstrapArchitecture": "x86", "type": "office-odt",
                "url": item["InstallerUrl"], "sha256": item["InstallerSha256"].upper(),
                "arguments": "/configure", "successCodes": [0, 3010]}
    version, manifest = get_manifest(package)
    portable = package == "TeamViewer.TeamViewer.QuickSupport"
    try:
        installer = select_installer(manifest, "x64", portable)
    except ValueError:
        if not app.get("allowX86"):
            raise
        installer = select_installer(manifest, "x86", portable)
    if portable:
        installer["sha256"] = official_quicksupport_hash(installer["url"])
        version = "Última oficial"
    return {**app, "available": True, "version": version, **installer}


def refresh():
    apps = json.loads((ROOT / "apps.json").read_text(encoding="utf-8"))["apps"]
    seen, results, failures = set(), [], []
    for app in apps:
        validate_app(app)
        if app["id"].lower() in seen:
            raise ValueError("ID duplicado: " + app["id"])
        seen.add(app["id"].lower())
        try:
            results.append(resolve(app))
        except Exception as error:
            # Fail closed: do not keep an obsolete installer or fall back to x86.
            results.append({**app, "available": False, "reason": str(error)[:300]})
            failures.append(app["id"])
    if failures:
        print("No disponibles: " + ", ".join(failures))
    output = {"schemaVersion": 2, "updatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(), "apps": results}
    (ROOT / "catalog-v2.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    legacy = []
    for app in results:
        if app.get("available") and (app.get("architecture") != "x64" or app.get("type") in ("portable", "office-odt")):
            legacy.append({"id": app["id"], "name": app["name"], "available": False, "reason": "Requiere ISOE v1.1.0 o posterior"})
        else:
            legacy.append(app)
    (ROOT / "catalog.json").write_text(json.dumps({**output, "schemaVersion": 1, "apps": legacy}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if not any(x["available"] for x in results):
        raise RuntimeError("Ninguna aplicación disponible: revisar acceso a WinGet")


if __name__ == "__main__":
    refresh()
