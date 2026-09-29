#!/usr/bin/env python3
"""Fails when XAML or C# in the UI project references a resource key that no resource dictionary defines.

WPF only reports a missing {StaticResource} at runtime, and this project's UI can only run on Windows,
so this check runs on every platform in CI instead. Dynamic keys (Localize.Get("Prefix." + x)) are checked
by prefix: at least one key with that prefix must exist.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent / "src" / "WinAppInspector.UI"
DICTS = [ROOT / "Resources" / "Strings.zh-CN.xaml", ROOT / "Resources" / "Theme.xaml", ROOT / "App.xaml"]

defined: set[str] = set()
problems: list[str] = []
for d in DICTS:
    keys = re.findall(r'x:Key="([^"]+)"', d.read_text(encoding="utf-8"))
    for key in sorted({k for k in keys if keys.count(k) > 1}):
        # WPF throws ArgumentException while materialising a dictionary with a duplicate key: the app never starts.
        problems.append(f"{d.name}: key '{key}' is defined more than once")
    defined.update(keys)

# Keys defined locally inside a view's own <UserControl.Resources> / <Window.Resources> count for that file.
for xaml in ROOT.rglob("*.xaml"):
    if xaml in DICTS or "obj" in xaml.parts or "bin" in xaml.parts:
        continue
    text = xaml.read_text(encoding="utf-8")
    local = set(re.findall(r'x:Key="([^"]+)"', text))
    for key in re.findall(r'\{StaticResource ([A-Za-z0-9_.]+)\}', text) + re.findall(r'StaticResource=\"([A-Za-z0-9_.]+)\"', text):
        if key not in defined and key not in local:
            problems.append(f"{xaml.relative_to(ROOT)}: StaticResource '{key}' is not defined")

for cs in ROOT.rglob("*.cs"):
    if "obj" in cs.parts or "bin" in cs.parts:
        continue
    text = cs.read_text(encoding="utf-8")
    for key in re.findall(r'Localize\.(?:Get|Format)\("([^"]+)"', text):
        if key.endswith("."):
            continue  # a prefix such as "Blocker." + code: checked below
        if key not in defined:
            problems.append(f"{cs.relative_to(ROOT)}: string key '{key}' is not defined")
    for prefix in re.findall(r'Localize\.Get\("([A-Za-z0-9_.]+)"\s*\+', text):
        if not any(k.startswith(prefix) for k in defined):
            problems.append(f"{cs.relative_to(ROOT)}: no string key starts with '{prefix}'")

if problems:
    print("\n".join(sorted(set(problems))))
    print(f"\n{len(set(problems))} missing resource reference(s).")
    sys.exit(1)

print(f"All resource references resolve ({len(defined)} keys defined).")
