#!/bin/sh

# Should match update-desktop-database.service
if [ -x /usr/bin/update-desktop-database ]; then
    /usr/bin/update-desktop-database -o $prefix/var/cache
fi

# Should match update-mime-database.service
if [ -x /usr/bin/update-mime-database ]; then
    /usr/bin/update-mime-database $prefix/usr/share/mime
fi
