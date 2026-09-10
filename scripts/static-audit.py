#!/usr/bin/env python3
"""Compatibility runner for the pre-flatten static audit.

The repository was flattened from src/ and tests/ into the sibling roots
Aegis.Cafeteria/ and Aegis.Cafeteria.Tests/.  The large historical audit is
preserved verbatim in static-audit.legacy.py; this runner translates only its
repository-path literals in memory before executing it so none of the existing
production guardrails are discarded during the layout transition.
"""
from pathlib import Path

script_path = Path(__file__).with_name("static-audit.legacy.py")
source = script_path.read_text(encoding="utf-8-sig")

replacements = (
    ("src/Aegis.Cafeteria", "Aegis.Cafeteria/Aegis.Cafeteria"),
    ("tests/Aegis.Cafeteria", "Aegis.Cafeteria.Tests/Aegis.Cafeteria"),
    ("root/'src'", "root/'Aegis.Cafeteria'"),
    ('root/"src"', 'root/"Aegis.Cafeteria"'),
    ("root / 'src'", "root / 'Aegis.Cafeteria'"),
    ('root / "src"', 'root / "Aegis.Cafeteria"'),
    ("root/'tests'", "root/'Aegis.Cafeteria.Tests'"),
    ('root/"tests"', 'root/"Aegis.Cafeteria.Tests"'),
    ("root / 'tests'", "root / 'Aegis.Cafeteria.Tests'"),
    ('root / "tests"', 'root / "Aegis.Cafeteria.Tests"'),
)

for old, new in replacements:
    source = source.replace(old, new)

exec(compile(source, str(script_path), "exec"), {"__name__": "__main__", "__file__": str(script_path)})
