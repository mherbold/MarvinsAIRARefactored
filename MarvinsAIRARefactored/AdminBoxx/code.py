import os
import time
import board
import busio
import usb_cdc
import supervisor

from adafruit_neotrellis.neotrellis import NeoTrellis
from adafruit_neotrellis.multitrellis import MultiTrellis

# Version
VERSION = "4.0.6"

# Configuration
num_rows = 4
num_columns = 8
heartbeat_timeout = 2

# Color constants
disabled = (0, 0, 0)
white = (32, 32, 32)
red = (32, 32, 32)
yellow = (32, 32, 0)
dark_red = (4, 0, 0)
dark_yellow = (4, 4, 0)

# Initialize I2C and trellis
i2c_bus = busio.I2C(board.SCL, board.SDA)
trellises = [[NeoTrellis(i2c_bus, addr=0x2E), NeoTrellis(i2c_bus, addr=0x2F)]]
trellis = MultiTrellis(trellises)

# Set all LEDs to one color
def set_all_leds(color):
    for y in range(num_rows):
        for x in range(num_columns):
            trellis.color(x, y, color)

# Utility: map (x, y) to board and key
def get_key_for(x, y):
    trellis_index = x // 4
    local_x = x % 4
    key = y * 4 + local_x
    return trellis_index, key

# Indicate startup
print("Starting up.")

set_all_leds(white)
time.sleep(0.4)
set_all_leds(disabled)
time.sleep(0.4)
set_all_leds(white)
time.sleep(0.4)
set_all_leds(disabled)
time.sleep(0.4)
set_all_leds(white)
time.sleep(0.4)

# Buffer for storing incoming data
buffer = bytearray()
usb_was_connected = True
app_was_connected = True
force_update_leds = True
last_command_time = 0

# Recovery button hold logic
recovery_button = (4, 0)
recovery_button_down = False
recovery_button_start = None

# Send a message to the PC app
def send_to_pc(message):
    usb_cdc.data.write((message + "\n").encode())
    print(f"[send_to_pc] {message}")

# Callback function for button presses
def on_press(x, y, edge):
    global app_was_connected, force_update_leds, recovery_button_down

    if edge == NeoTrellis.EDGE_RISING:
        if (x, y) == recovery_button:
            print("Recovery button down")
            recovery_button_down = True
            
        if app_was_connected:
            send_to_pc(f":{y},{x}")
            
        else:
            force_update_leds = True
            
    else:
        if (x, y) == recovery_button:
            print("Recovery button up")
            recovery_button_down = False

def non_blocking_sleep(duration):
    start = time.monotonic()

    while time.monotonic() - start < duration:
        if usb_cdc.data.in_waiting:
            return
            
        trellis.sync()
        time.sleep(0.01)

# Function to flash a button 3 times
def flash_led(x, y, color):
    trellis.color(x, y, color)
    non_blocking_sleep(0.5)
    trellis.color(x, y, disabled)
    non_blocking_sleep(0.5)
    trellis.color(x, y, color)
    non_blocking_sleep(0.5)
    trellis.color(x, y, disabled)
    non_blocking_sleep(0.5)
    trellis.color(x, y, color)
    non_blocking_sleep(0.5)

# Set callback on all buttons
for y in range(num_rows):
    for x in range(num_columns):
        trellis.activate_key(x, y, NeoTrellis.EDGE_RISING)
        trellis.activate_key(x, y, NeoTrellis.EDGE_FALLING)
        trellis.set_callback(x, y, on_press)
        
for y in range(num_rows):
    for x in range(num_columns):
        trellis.set_callback(x, y, on_press)
        
# Successful startup
print("Started up OK.")
set_all_leds(disabled)

# Main loop
while True:
    current_time = time.monotonic()
    trellis.sync()

    # Recovery button hold check
    trellis_idx, key_idx = get_key_for(*recovery_button)
    trellis_board = trellises[0][trellis_idx]

    if recovery_button_down:
        if recovery_button_start is None:
            recovery_button_start = current_time
            
        elif current_time - recovery_button_start >= 5:
            recovery_button_start = None
            print("Enabling USB drive.")

            try:
                with open("/enable_usb_drive.txt", "w") as f:
                    f.write("1")
                    
                time.sleep(1)
                supervisor.reload()
                
            except Exception as e:
                print(f"Failed to create enable_usb_drive.txt file: {e}")
                
    else:
        recovery_button_start = None
        
    while usb_cdc.data.in_waiting:
        current_time = time.monotonic()
        byte = usb_cdc.data.read(1)

        if byte:
            buffer.append(byte[0])

            if byte[0] == 255:  # End of message

                # keep-alive (command 128)
                if len(buffer) == 2 and buffer[0] == 128:
                    last_command_time = current_time
                    
                # set LED color (command 129)
                elif len(buffer) == 6 and buffer[0] == 129:
                    led = buffer[1]
                    r = buffer[2]
                    g = buffer[3]
                    b = buffer[4]

                    if 0 <= led < 32 and all(v <= 127 for v in (r, g, b)):
                        x = led % num_columns
                        y = led // num_columns
                        trellis.color(x, y, (r, g, b))
                        last_command_time = current_time
                        
                    else:
                        print("Invalid values for LED color command.")
                        
                # send version (command 130)
                elif len(buffer) == 2 and buffer[0] == 130:
                    send_to_pc(f"V{VERSION}")
                    
                # start update (command 131)
                elif len(buffer) == 2 and buffer[0] == 131:
                    print("Starting new update.")
                    
                    try:
                        os.stat("/update.py")
                        os.remove("/update.py")
                        print("Removed previous update.py file.")
                        
                    except OSError:
                        print("No existing update.py file to remove.")
                            
                    send_to_pc("N")
                        
                # next update line (command 132)
                elif len(buffer) >= 2 and buffer[0] == 132:
                    try:
                        line = buffer[1:-1].decode("ascii", "ignore")

                        with open("/update.py", "a") as f:
                            f.write(line + "\n")
                            
                        print("Appended line to update.temp file.")
                        send_to_pc("N")
                        
                    except Exception as e:
                        print(f"Error writing line to update.temp file: {e}")
                        
                # complete update and reboot (command 133)
                elif len(buffer) == 2 and buffer[0] == 133:
                    try:
                        os.stat("/update.py")
                        
                        with open("/update.py", "r") as src:
                            contents = src.read()
                            
                        with open("/code.py", "w") as dst:
                            dst.write(contents) 

                        print("Overwrote the code.py with update.py contents.")
                        time.sleep(1)
                        supervisor.reload()
                        
                    except OSError:
                        print("update.py not found, skipping rename")
                        
                # invalid command
                else:
                    print("Malformed command received.")
                    
                # clear buffer
                buffer = bytearray()

    usb_connected = supervisor.runtime.usb_connected
    app_connected = current_time - last_command_time < heartbeat_timeout

    usb_changed = usb_connected != usb_was_connected
    app_changed = app_connected != app_was_connected

    if usb_changed or (app_changed and not app_connected) or force_update_leds:
        set_all_leds(disabled)

        if not usb_connected:
            flash_led(3, 3, red)

        else:
            flash_led(3, 3, yellow)
            
        non_blocking_sleep(300)

        if not usb_connected:
            trellis.color(3, 3, dark_red)

        else:
            trellis.color(3, 3, dark_yellow)

        force_update_leds = False

    usb_was_connected = usb_connected
    app_was_connected = app_connected

    time.sleep(0.01)
