#!/usr/bin/env python3
"""Independent one-ZIP provenance, deliberately incompatible with full release evidence."""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from release_names import owner_filename

def expected_name(version):
    return owner_filename(version,"Windows-32bit","Portable")

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream,"sha256").hexdigest()

def valid_sha(value):
    return isinstance(value,str) and re.fullmatch(r"[0-9a-f]{40}",value) is not None

def inventory(root, names):
    return root.is_dir() and set(p.name for p in root.iterdir()) == set(names) and all(
        p.is_file() and not p.is_symlink() for p in root.iterdir())

def create(root, version, checkout, head):
    name=expected_name(version)
    if not valid_sha(checkout) or not valid_sha(head) or not inventory(root,[name]):
        raise ValueError("Development requires exactly one actual x86 Portable ZIP and exact SHA identities")
    package=root/name
    if package.stat().st_size <= 0:
        raise ValueError("Empty development archive")
    sha=digest(package)
    data={"buildMode":"development","finalRelease":False,"version":version,
          "commit":checkout,"sourceHeadSha":head,"targetArchitecture":"Windows-32bit",
          "runtimeIdentifier":"win-x86","files":[{"file":name,"bytes":package.stat().st_size,"sha256":sha}]}
    (root/"DEVELOPMENT_METADATA.json").write_text(json.dumps(data,indent=2)+"\n",encoding="utf-8")
    (root/"SHA256SUMS.txt").write_text(f"{sha}  {name}\n",encoding="utf-8")

def verify(root, version, checkout, head):
    try:
        name=expected_name(version)
        if not valid_sha(checkout) or not valid_sha(head):
            raise ValueError("Invalid commit identity")
        if not inventory(root,[name,"DEVELOPMENT_METADATA.json","SHA256SUMS.txt"]):
            raise ValueError("Unexpected, missing or linked development artifact")
        package=root/name
        size=package.stat().st_size
        if size <= 0:
            raise ValueError("Empty development archive")
        sha=digest(package)
        expected={"buildMode":"development","finalRelease":False,"version":version,
                  "commit":checkout,"sourceHeadSha":head,"targetArchitecture":"Windows-32bit",
                  "runtimeIdentifier":"win-x86","files":[{"file":name,"bytes":size,"sha256":sha}]}
        meta=json.loads((root/"DEVELOPMENT_METADATA.json").read_text(encoding="utf-8"))
        if meta != expected or meta.get("finalRelease") is not False:
            raise ValueError("Development metadata does not match exact package and source")
        if (root/"SHA256SUMS.txt").read_text(encoding="utf-8") != f"{sha}  {name}\n":
            raise ValueError("Development SHA-256 mismatch")
        return []
    except (OSError,ValueError,TypeError,AttributeError) as exc:
        return [str(exc)]

def main(argv):
    if len(argv)!=6 or argv[1] not in ("create","verify"):
        print("usage: development-artifact.py create|verify DIR VERSION CHECKOUT_SHA SOURCE_HEAD_SHA")
        return 2
    action, directory, version, checkout, head=argv[1:]
    try:
        actual=subprocess.check_output(["git","rev-parse","HEAD"],text=True).strip()
        if actual!=checkout:
            raise ValueError("Current checkout does not equal tested GitHub SHA")
        root=Path(directory)
        if action=="create":
            create(root,version,checkout,head)
        errors=verify(root,version,checkout,head)
        if errors:
            raise ValueError("; ".join(errors))
    except (OSError,ValueError,subprocess.SubprocessError) as exc:
        print("DEVELOPMENT PROVENANCE FAIL:",exc)
        return 1
    print("DEVELOPMENT PROVENANCE PASS: one x86 Portable ZIP; NOT full release acceptance")
    return 0

if __name__=="__main__":
    sys.exit(main(sys.argv))
