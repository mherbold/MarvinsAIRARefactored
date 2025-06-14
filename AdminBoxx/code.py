import time
import board
import busio
import usb_cdc
import supervisor

from adafruit_neotrellis.neotrellis import NeoTrellis
from adafruit_neotrellis.multitrellis import MultiTrellis

# Configuration
num_rows = 4
num_columns = 8
heartbeat_timeout = 2

# Color constants
white = (16, 16, 16)
black = (0, 0, 0)
red = (16, 0, 0)
yellow = (16, 16, 0)

# Initialize I2C and trellis
i2c_bus = busio.I2C(board.SCL, board.SDA)
trellises = [[NeoTrellis(i2c_bus, addr=0x2E), NeoTrellis(i2c_bus, addr=0x2F)]]
trellis = MultiTrellis(trellises)

# Set all LEDs to one color
def set_all_leds(color):
    for y in range(num_rows):
        for x in range(num_columns):
            trellis.color(x, y, color)

# Indicate startup
set_all_leds(white)
print("Starting up.")

# Send a message to the PC app
def send_to_pc(message):
    print(f"Sending to PC: {message}")
    usb_cdc.data.write((message + "\n").encode())


# Callback function for button presses
def on_press(x, y, edge):
    if edge:
        send_to_pc(f":{y},{x}")


# Set callback on all buttons
for y in range(num_rows):
    for x in range(num_columns):
        trellis.activate_key(x, y, NeoTrellis.EDGE_RISING)
        trellis.set_callback(x, y, on_press)

# Buffer for storing incoming data
buffer = bytearray()
usb_was_connected = True
app_was_connected = True
last_command_time = time.monotonic()

# Successful startup
print("Started up OK.")
set_all_leds(black)

# Main loop
while True:
    current_time = time.monotonic()
    trellis.sync()

    while usb_cdc.data.in_waiting:
        current_time = time.monotonic()
        byte = usb_cdc.data.read(1)
        if byte:
            buffer.append(byte[0])
            if byte[0] == 255:  # End of message
                # ping command
                if len(buffer) == 2 and buffer[0] == 128:
                    last_command_time = current_time

                # set LED color command
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

                # invalid command
                else:
                    print("Malformed command received.")

                buffer = bytearray()

    usb_connected = supervisor.runtime.usb_connected
    if usb_connected != usb_was_connected:
        if not usb_connected:
            set_all_leds(red)
    usb_was_connected = usb_connected

    app_connected = (current_time - last_command_time < heartbeat_timeout)
    if usb_connected:
        if app_connected != app_was_connected:
            print(app_connected)
            if not app_connected:
                set_all_leds(yellow)
    app_was_connected = app_connected

    time.sleep(0.01)
