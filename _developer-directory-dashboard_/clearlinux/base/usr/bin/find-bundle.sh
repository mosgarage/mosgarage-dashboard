#!/bin/sh
if [ ! -d /usr/share/clear/allbundles ]; then
	echo "allbundles not found; please install it."
else
	grep -v "completions" /usr/share/clear/allbundles/* | grep "/usr/bin.*\\W"$1"\\W"
fi
