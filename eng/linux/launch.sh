#!/bin/sh
set -eu
base="$(dirname "$(readlink -f -- "$0")")"
exec "$base/App/Drivebrew" "$@"
