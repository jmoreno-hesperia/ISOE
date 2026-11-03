"""Issue contents are parsed as data, never interpolated into shell commands."""
import json
import os
import re
from catalog import ROOT, resolve, validate_app


def parse(body):
    fields = dict(re.findall(r"^### ([^\r\n]+)\r?\n\r?\n(.*?)(?=\r?\n### |\Z)", body, re.M | re.S))
    app = {"id": fields.get("ID de WinGet", "").strip(), "name": fields.get("Nombre", "").strip(),
           "publisher": fields.get("Fabricante", "").strip()}
    if app["publisher"] == "_No response_":
        app["publisher"] = ""
    validate_app(app)
    return app


if __name__ == "__main__":
    event = json.loads(open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8").read())
    app = parse(event["issue"]["body"])
    path = ROOT / "apps.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    if any(x["id"].lower() == app["id"].lower() for x in data["apps"]):
        raise ValueError("El ID ya está en el catálogo")
    resolve(app)  # Reject missing IDs, unsupported x64 and unknown silent switches.
    data["apps"].append(app)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
