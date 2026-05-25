import usb_cdc
import storage
import os
import time
import supervisor

# Enable serial console and data channels
usb_cdc.enable(console=True, data=True)

# Wait briefly to ensure file system is ready
time.sleep(0.5)

# Enable or disable the USB drive
if "enable_usb_drive.txt" in os.listdir("/"):
    print("Enabling USB drive.")
    storage.enable_usb_drive()

else:
    print("Disabling USB drive.")
    storage.disable_usb_drive()

