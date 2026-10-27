"""Promote approved buttons from BauTools into ARYE Office Tools.

BauTools is the development repo; this repo is a copy of the buttons approved for the office.
Never edit promoted files here: change them in BauTools, approve, and promote again.

    python build/promote.py --bautools "C:/ARYE ai/BauTools" --ref origin/main
    python build/promote.py --bautools "C:/ARYE ai/BauTools" --ref origin/main --discover

Reads build/promote.json. Files are read from git objects at --ref (never from a working tree,
so unfinished local edits in BauTools are not promoted). Each file is renamed into this
assembly (ZoningFloorArea -> ARYE.OfficeTools, BauTools.Core -> ARYE.OfficeTools.Core).

--discover recomputes the file list: starting from each button's seed files, it builds this
project and adds the BauTools file that declares each missing type until the build is clean.
Then it keeps the BauTools tests that compile against what was promoted.

Without --discover it copies the manifest's files and tests, deletes promoted files that are
no longer listed, builds, runs the tests and writes PROMOTED.md and PromotedFrom.cs.
"""
import argparse, datetime, json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src", "ARYE.OfficeTools")
TESTS = os.path.join(ROOT, "tests", "ARYE.OfficeTools.Tests")
CSPROJ = os.path.join(SRC, "ARYE.OfficeTools.csproj")
TEST_CSPROJ = os.path.join(TESTS, "ARYE.OfficeTools.Tests.csproj")
MANIFEST = os.path.join(ROOT, "build", "promote.json")

# Files that belong to this repo and are never overwritten or deleted by a promotion.
OWN_SRC = {"App.cs", "ARYE.OfficeTools.csproj", "PromotedFrom.cs", "Commands/AboutCommand.cs"}
OWN_TESTS = {"ARYE.OfficeTools.Tests.csproj"}
NEVER_PROMOTE = {"src/ZoningFloorArea/App.cs", "src/ZoningFloorArea/Commands/AboutCommand.cs",
                 "src/ZoningFloorArea/ApiCompileChecks.cs"}


def git(bt, *args, binary=False):
    out = subprocess.run(["git", "-C", bt, *args], capture_output=True)
    if out.returncode != 0:
        sys.exit("git " + " ".join(args) + " failed: " + out.stderr.decode(errors="ignore"))
    return out.stdout if binary else out.stdout.decode("utf-8", errors="ignore")


def rename(text):
    text = re.sub(r"\bBauTools\.Core\.Tests\b", "ARYE.OfficeTools.Tests", text)
    text = re.sub(r"\bBauTools\.Core\b", "ARYE.OfficeTools.Core", text)
    text = re.sub(r"\bZoningFloorArea\b", "ARYE.OfficeTools", text)
    return text.replace(";assembly=ARYE.OfficeTools.Core", "")


def target(rel):
    for prefix, dest in (("src/ZoningFloorArea/", SRC), ("src/BauTools.Core/", os.path.join(SRC, "Core")),
                         ("tests/BauTools.Core.Tests/", TESTS)):
        if rel.startswith(prefix):
            return os.path.join(dest, rel[len(prefix):])
    raise ValueError("not promotable: " + rel)


def copy(bt, ref, rel):
    data = git(bt, "show", f"{ref}:{rel}", binary=True)
    if rel.endswith((".cs", ".xaml")):
        bom = data[:3] == b"\xef\xbb\xbf"
        data = (b"\xef\xbb\xbf" if bom else b"") + rename(data.decode("utf-8-sig")).encode("utf-8")
    dst = target(rel)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(dst, "wb") as f:
        f.write(data)


def partners(rel, tree):
    out = {rel}
    if rel.endswith(".xaml.cs"):
        out.add(rel[:-3])
    elif rel.endswith(".xaml"):
        out.add(rel + ".cs")
    return {r for r in out if r in tree}


def build(csproj, config="Release"):
    return subprocess.run(["dotnet", "build", csproj, "-c", config, "-nologo", "-v", "q"],
                          capture_output=True, text=True).stdout


def discover(bt, ref, manifest):
    tree = set(git(bt, "ls-tree", "-r", "--name-only", ref).splitlines())
    decl = re.compile(r"\b(?:class|interface|enum|struct|record|delegate\s+\S+)\s+([A-Z][A-Za-z0-9_]*)")
    index = {}
    for rel in tree:
        if rel.endswith(".cs") and rel.startswith(("src/ZoningFloorArea/", "src/BauTools.Core/")) \
                and rel not in NEVER_PROMOTE:
            for name in decl.findall(git(bt, "show", f"{ref}:{rel}")):
                index.setdefault(name, set()).add(rel)

    have = set()
    for b in manifest["buttons"]:
        for s in b["seeds"]:
            have |= partners(s, tree)
    for s in manifest.get("extra_seeds", []):
        have |= partners(s, tree)
    clear_promoted()
    for r in have:
        copy(bt, ref, r)

    pats = [r"error CS0246: The type or namespace name '(\w+)", r"error CS0103: The name '(\w+)' does not exist",
            r"error MC3074: The tag '(\w+)' does not exist", r"error MC3050: Cannot find the type '(?:\w+:)?(\w+)'"]
    for it in range(40):
        out = build(CSPROJ)
        missing = set()
        for p in pats:
            missing |= set(re.findall(p, out))
        added = {r for n in missing for f in index.get(n, ()) for r in partners(f, tree)} - have
        errors = sorted({l.strip() for l in out.splitlines() if ": error " in l})
        print(f"  pass {it}: {len(errors)} errors, {len(added)} files added")
        if not added:
            if errors:
                print("\n".join("    " + e[:240] for e in errors[:30]))
                sys.exit("Build still fails. A whole namespace may be missing (CS0234): add one of its "
                         "files to extra_seeds in build/promote.json and run --discover again.")
            break
        for r in added:
            copy(bt, ref, r)
        have |= added

    core = {p.split("/")[2] for p in have if p.startswith("src/BauTools.Core/")}
    tests = []
    for rel in sorted(tree):
        if rel.startswith("tests/BauTools.Core.Tests/") and rel.endswith(".cs"):
            used = set(re.findall(r"using BauTools\.Core\.(\w+)", git(bt, "show", f"{ref}:{rel}")))
            if used and used <= core:
                tests.append(rel)
                copy(bt, ref, rel)
    for it in range(10):
        out = build(TEST_CSPROJ, "Debug")
        bad = set(re.findall(r"tests[\\/]ARYE\.OfficeTools\.Tests[\\/]([^\(\r\n\]]+?\.cs)\(\d+,\d+\): error", out))
        if not bad:
            break
        for b in bad:
            b = b.replace("\\", "/")
            os.remove(os.path.join(TESTS, b))
            tests.remove("tests/BauTools.Core.Tests/" + b)
            print("  test left out (does not compile without unpromoted code):", b)

    manifest["files"] = sorted(have)
    manifest["tests"] = tests
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"Manifest updated: {len(have)} files, {len(tests)} test files.")


def promoted_on_disk():
    found = []
    for base, own in ((SRC, OWN_SRC), (TESTS, OWN_TESTS)):
        for dp, dirs, fs in os.walk(base):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
            for f in fs:
                rel = os.path.relpath(os.path.join(dp, f), base).replace("\\", "/")
                if rel not in own:
                    found.append(os.path.join(dp, f))
    return found


def clear_promoted():
    for p in promoted_on_disk():
        os.remove(p)


def promote(bt, ref, manifest, sha):
    clear_promoted()
    for rel in manifest["files"] + manifest["tests"]:
        copy(bt, ref, rel)

    with open(os.path.join(SRC, "PromotedFrom.cs"), "w", encoding="utf-8") as f:
        f.write("namespace ARYE.OfficeTools\n{\n"
                "    /// <summary>Written by build/promote.py: the BauTools commit these buttons were copied from.</summary>\n"
                "    internal static class PromotedFrom\n    {\n"
                f"        public const string Commit = \"{sha}\";\n"
                f"        public const string Date = \"{datetime.date.today().isoformat()}\";\n"
                "    }\n}\n")

    out = build(CSPROJ)
    if "Build succeeded" not in out or " warning " in out:
        print(out[-4000:])
        sys.exit("The promoted code does not build cleanly. Nothing was committed.")
    test = subprocess.run(["dotnet", "test", TEST_CSPROJ, "-nologo"], capture_output=True, text=True).stdout
    summary = [l.strip() for l in test.splitlines() if "Passed!" in l or "Failed!" in l]
    if not summary or "Failed!" in summary[-1]:
        print(test[-4000:])
        sys.exit("Tests fail after the promotion.")

    lines = ["# Promoted buttons", "",
             "Written by `build/promote.py`. Every file under `src/` and `tests/` except `App.cs`, "
             "`Commands/AboutCommand.cs` and the project files is a copy from BauTools: change it there.", "",
             f"- **BauTools commit:** `{sha}` ({ref})",
             f"- **Promoted on:** {datetime.date.today().isoformat()}",
             f"- **Files:** {len(manifest['files'])} · **test files:** {len(manifest['tests'])} · {summary[-1]}", "",
             "| Panel | Button | Command |", "|---|---|---|"]
    for b in manifest["buttons"]:
        lines.append(f"| {b['panel']} | {b['name']} | `{b['seeds'][0].split('/')[-1]}` |")
    lines += ["", "Deferred: " + "; ".join(manifest.get("deferred", [])) if manifest.get("deferred") else ""]
    with open(os.path.join(ROOT, "PROMOTED.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines).rstrip() + "\n")
    print(f"Promoted {len(manifest['buttons'])} buttons from BauTools {sha[:7]}: build clean, {summary[-1]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bautools", required=True, help="path to a BauTools git checkout")
    ap.add_argument("--ref", default="origin/main", help="commit, tag or branch to promote from")
    ap.add_argument("--discover", action="store_true", help="recompute the file list before promoting")
    a = ap.parse_args()
    manifest = json.load(open(MANIFEST, encoding="utf-8"))
    git(a.bautools, "fetch", "-q", "origin")
    sha = git(a.bautools, "rev-parse", a.ref).strip()
    if a.discover:
        discover(a.bautools, a.ref, manifest)
    promote(a.bautools, a.ref, manifest, sha)


if __name__ == "__main__":
    main()
