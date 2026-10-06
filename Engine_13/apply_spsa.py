#!/usr/bin/env python3
"""Writes SPSA-tuned search parameters into search.cpp.

    python3 apply_spsa.py tuned_params.txt search.cpp

tuned_params.txt is what the match runner's --spsa-out writes: one parameter per
line, "NAME value min max c_end". For each NAME, the default value in
"SEARCH_PARAM(NAME, value, min, max)" in search.cpp is replaced; the range and
everything else are left alone. A name search.cpp does not have is an error, so
nothing is silently lost.
"""

import re
import sys


def main(params_path: str, source_path: str) -> None:
    source = open(source_path).read()
    changed = 0
    for line in open(params_path):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        name, value = line.split()[:2]
        value = str(round(float(value)))
        pattern = re.compile(r"SEARCH_PARAM\(" + name + r", (-?\d+),")
        if not pattern.search(source):
            sys.exit(f"{name} is not a SEARCH_PARAM in {source_path}")
        source = pattern.sub(f"SEARCH_PARAM({name}, {value},", source, count=1)
        changed += 1
    open(source_path, "w").write(source)
    print(f"set {changed} search parameters in {source_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: apply_spsa.py tuned_params.txt search.cpp")
    main(sys.argv[1], sys.argv[2])
