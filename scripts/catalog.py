"""Resolve only unattended x64 installers from microsoft/winget-pkgs. No binaries in Git."""
import datetime
import json
import os
from pathlib import Path
import re
import urllib.request
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


def select_installer(manifest):
    candidates = []
    for item in manifest.get("Installers", []):
        merged = {**manifest, **item}
        # User-only and dependency-bearing packages require extra orchestration.
        if merged.get("Architecture") != "x64" or merged.get("Scope") == "user":
            continue
        if merged.get("Dependencies") or merged.get("InstallerType") not in (*DEFAULTS, "exe"):
            continue
        if merged.get("InstallModes") and "silent" not in merged["InstallModes"]:
            continue
        switches = {**manifest.get("InstallerSwitches", {}), **item.get("InstallerSwitches", {})}
        kind = merged["InstallerType"]
        silent = switches.get("Silent") or DEFAULTS.get(kind)
        if not silent:
            continue
        args = " ".join(filter(None, [silent, switches.get("Custom", "")]))
        # Placeholders that require WinGet substitution are not executable here.
        if "<" in args or ">" in args or "\n" in args or "\r" in args:
            continue
        url, sha = merged.get("InstallerUrl", ""), merged.get("InstallerSha256", "")
        if not url.startswith("https://") or not re.fullmatch(r"[a-fA-F0-9]{64}", sha):
            continue
        candidates.append({
            "architecture": "x64", "url": url, "sha256": sha.upper(),
            "type": kind, "arguments": args,
            "successCodes": sorted(set([0, 1641, 3010] + merged.get("InstallerSuccessCodes", []))),
            "locale": merged.get("InstallerLocale", ""),
        })
    candidates.sort(key=lambda x: (x["locale"] not in ("es-ES", "es"), x["locale"] not in ("", "en-US")))
    if not candidates:
        raise ValueError("Sin instalador x64 silencioso compatible (o requiere dependencias/WinGet).")
    result = candidates[0]
    del result["locale"]
    return result


def resolve(app):
    validate_app(app)
    package = app["id"]
    path = "manifests/" + package[0].lower() + "/" + package.replace(".", "/")
    listing = json.loads(get("https://api.github.com/repos/microsoft/winget-pkgs/contents/" + path))
    versions = [x["name"] for x in listing if x["type"] == "dir"]
    if not versions:
        raise ValueError("Paquete sin versiones")
    version = max(versions, key=version_key)
    base = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/" + path + "/" + version + "/" + package
    manifest = yaml.safe_load(get(base + ".installer.yaml"))
    if manifest.get("PackageIdentifier") != package or str(manifest.get("PackageVersion")) != version:
        raise ValueError("Identidad de manifiesto incorrecta")
    return {**app, "available": True, "version": version, **select_installer(manifest)}


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
    output = {"schemaVersion": 1, "updatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(), "apps": results}
    (ROOT / "catalog.json").write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if not any(x["available"] for x in results):
        raise RuntimeError("Ninguna aplicación disponible: revisar acceso a WinGet")


if __name__ == "__main__":
    refresh()
