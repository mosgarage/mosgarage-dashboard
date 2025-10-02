#!/bin/sh

if [ -n "$1" ]; then
	echo "Usage: $0"
	echo "    Creates motd parts for notifying the user what services need restarting."
	echo "    This program takes no options"
	exit 0
fi

MOTDD="/run/motd.d"
MOTDF="$MOTDD/clr-service-restart.motd"

mkdir -p "$MOTDD"

if [ -n "`clr-service-restart -a -n 2>&1`" ]; then
	echo -e " * Some system services need a restart.\n   Run \`sudo clr-service-restart -a -n\` to view them." > "$MOTDF"
else
	> "$MOTDF"
fi

