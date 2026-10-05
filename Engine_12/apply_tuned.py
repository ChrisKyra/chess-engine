#!/usr/bin/env python3
"""Writes tuned weights into eval.cpp.

    ./tuner tune positions.txt 2000 tuned.txt
    python3 apply_tuned.py tuned.txt eval.cpp

tuned.txt holds C++ definitions in the same form as eval.cpp ("const int NAME[...] =
{ ... };" and "constexpr int NAME = value;"). Each one replaces the definition of the
same name in eval.cpp; comments and everything else are left as they are. A name the
tuner wrote that eval.cpp does not have is an error, so nothing is silently lost.
"""

import re
import sys

DEFINITION = re.compile(r"^(const int|constexpr int) (\w+)(\[[^\]]*\])? = (\{.*?\}|[-\d]+);", re.S | re.M)


def main(tuned_path: str, eval_path: str) -> None:
    tuned = open(tuned_path).read()
    source = open(eval_path).read()
    replaced = 0
    for match in DEFINITION.finditer(tuned):
        name = match.group(2)
        pattern = re.compile(r"^(const int|constexpr int) " + name + r"(\[[^\]]*\])? = (\{.*?\}|[-\d]+);", re.S | re.M)
        if not pattern.search(source):
            sys.exit(f"{name} is not defined in {eval_path}")
        source = pattern.sub(lambda _: match.group(0), source, count=1)
        replaced += 1
    open(eval_path, "w").write(source)
    print(f"replaced {replaced} definitions in {eval_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: apply_tuned.py tuned.txt eval.cpp")
    main(sys.argv[1], sys.argv[2])
