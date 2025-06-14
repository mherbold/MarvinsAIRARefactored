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

# Buffer for storing incoming data
buffer = bytearray()
usb_was_connected = True
app_was_connected = True
turn_off_screen = False
force_update_leds = True
last_command_time = time.monotonic()

# Send a message to the PC app
def send_to_pc(message):
    usb_cdc.data.write((message + "\n").encode())


# Callback function for button presses
def on_press(x, y, edge):
    global app_was_connected, turn_off_screen, force_update_leds

    if edge:

        if app_was_connected:
            send_to_pc(f":{y},{x}")

        else:
            turn_off_screen = not turn_off_screen

            if turn_off_screen:
                set_all_leds(black)

            else:
                force_update_leds = True


# Set callback on all buttons
for y in range(num_rows):
    for x in range(num_columns):
        trellis.activate_key(x, y, NeoTrellis.EDGE_RISING)
        trellis.set_callback(x, y, on_press)

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
    app_connected = current_time - last_command_time < heartbeat_timeout

    usb_changed = usb_connected != usb_was_connected
    app_changed = app_connected != app_was_connected

    if usb_changed or app_changed or force_update_leds:
        force_update_leds = False
        turn_off_screen = False

        if not usb_connected:
            set_all_leds(red)

        elif not app_connected:
            set_all_leds(yellow)

    usb_was_connected = usb_connected
    app_was_connected = app_connected

    time.sleep(0.01)
