#!/sbin/sh

MODDIR=${0%/*}
exec /system/bin/app_process -cp ${MODDIR}/superpower.apk /sdcard Superpower
