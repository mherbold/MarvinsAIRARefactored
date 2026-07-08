import usb_cdc
import storage
import os
import time

# AdminBoxx boot.py - version 2
#
# SimHub protection: SimHub's Arduino feature opens every free COM port and
# writes its protocol handshake, which begins with byte 0x03. On the
# CircuitPython console (REPL) port, 0x03 is Ctrl-C, which raises
# KeyboardInterrupt, stops code.py, and leaves the board dead at the REPL
# until it is power cycled. To protect against this, the console port is only
# enabled in recovery mode - in normal operation only the data port exists,
# and the data port treats 0x03 as ordinary data.
#
# IMPORTANT: Keep this file identical to the BOOT_PY constant in code.py -
# code.py rewrites boot.py on the device whenever the two differ.

# Wait briefly to ensure file system is ready
time.sleep(0.5)

# Recovery mode (hold the recovery button for 5 seconds) enables the USB drive
# and the serial console; normal mode disables both.
if "enable_usb_drive.txt" in os.listdir("/"):
    print("Recovery mode - enabling USB drive and serial console.")
    usb_cdc.enable(console=True, data=True)
    storage.enable_usb_drive()

else:
    print("Normal mode - disabling USB drive and serial console.")
    usb_cdc.enable(console=False, data=True)
    storage.disable_usb_drive()
