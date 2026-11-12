#!/bin/sh
# Installed by BorgMate as root. sudo lets the user run only this script without a password,
# so it accepts nothing beyond scheduling or cancelling BorgMate's own wake events.
set -eu
if [ "$#" -ne 2 ]; then
    echo "usage: borgmate-pmset schedule|cancel 'MM/dd/yy HH:mm:ss'" >&2
    exit 64
fi
case "$2" in
    [0-9][0-9]/[0-9][0-9]/[0-9][0-9]' '[0-9][0-9]:[0-9][0-9]:[0-9][0-9]) ;;
    *) echo "borgmate-pmset: invalid date '$2'" >&2; exit 64 ;;
esac
case "$1" in
    schedule) exec /usr/bin/pmset schedule wakeorpoweron "$2" BorgMate ;;
    cancel) exec /usr/bin/pmset schedule cancel wakeorpoweron "$2" BorgMate ;;
    *) echo "borgmate-pmset: unknown action '$1'" >&2; exit 64 ;;
esac
