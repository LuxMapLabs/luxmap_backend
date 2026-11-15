#!/bin/sh
# Offline macOS renderer: Python 3 stdlib, an installed Cairo library, and sips.
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$script_dir/render-icons.py"
