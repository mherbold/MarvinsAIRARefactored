// SBT - Sim Racing Belt Tensioner
//
// Controller sketch for two DS51150 12V digital servos on an Arduino Nano.
// These servos are advertised as 180-degree servos using a 500-2500 microsecond
// pulse range with 1500 microseconds as the neutral (center) position.
//
// ---------------------------------------------------------------------------
// Serial protocol (line-based, any common line ending):
//
//   WHAT ARE YOU?       -> responds with "MAIRA SBT"
//
//   SLxxxxRyyyy         -> Set target positions
//                          xxxx = left tenths-of-a-degree (0000-1800)
//                          yyyy = right tenths-of-a-degree (0000-1800)
//                          Values are clamped to configured min/max limits.
//
//   NLxxxxRyyyy         -> Set neutral positions (used by timeout-to-neutral)
//                          Values are clamped to configured min/max limits.
//
//   ALxxxxRyyyy         -> Set minimum position limits
//                          Existing neutral/current/target positions are
//                          re-clamped so nothing remains outside the new range.
//
//   BLxxxxRyyyy         -> Set maximum position limits
//                          Same re-clamping behavior as the A command.
//
//   MLxxxxRyyyy         -> Set maximum movement per update (velocity limiter)
//                          xxxx = left max tenths-of-a-degree per update (0005-0050)
//                          yyyy = right max tenths-of-a-degree per update (0005-0050)
//                          Values are clamped to 5-50 and persisted to EEPROM.
//
// ---------------------------------------------------------------------------
// Timeout-to-neutral behavior:
//
//   If no valid S command is received for SERIAL_TIMEOUT_MS milliseconds the
//   controller automatically targets both servos to their saved neutral
//   positions. The servos stay at neutral until new S commands arrive.
//
// ---------------------------------------------------------------------------
// Motion model (velocity-limited):
//
//   Positions are represented internally in tenths of a degree (0-1800).
//   Each MOTION_UPDATE_INTERVAL_MS the current position moves toward the
//   target position by at most MAX_MOVEMENT_PER_UPDATE tenths of a degree.
//   This prevents sudden jumps and provides smooth belt tensioning.
//
// ---------------------------------------------------------------------------

#include <Servo.h>
#include <EEPROM.h>

// --- Pin assignments ---

const int LEFT_SERVO_PIN = 10;    // Left servo signal wire on D9
const int RIGHT_SERVO_PIN = 9;  // Right servo signal wire on D10

// --- Serial settings ---

const long SERIAL_BAUD_RATE = 115200;

// --- Timing constants ---

const unsigned long SERIAL_TIMEOUT_MS = 1000;        // Timeout before returning to neutral
const unsigned long MOTION_UPDATE_INTERVAL_MS = 10;   // Milliseconds between motion updates

// --- Motion defaults ---

const int DEFAULT_MAX_MOVEMENT_PER_UPDATE = 10;  // Default tenths-of-a-degree per motion update

// --- Pulse width mapping constants (microseconds) ---

const int PULSE_US_AT_0 = 500;     // Pulse width at 0 tenths (0.0 degrees)
const int PULSE_US_AT_1800 = 2500; // Pulse width at 1800 tenths (180.0 degrees)

// --- EEPROM layout ---
//
//   A signature byte and version byte appear first so the sketch can detect
//   whether the EEPROM has ever been written by this firmware version.
//   If either marker is wrong the stored data is treated as invalid and the
//   factory defaults are used (and then immediately written back to EEPROM
//   so the device self-heals on the next power cycle).
//
//   All six calibration integers are stored as 16-bit little-endian values.
//
//   Address  Size  Description
//   -------  ----  -----------
//   0        1     Signature byte (must equal EEPROM_SIGNATURE)
//   1        1     Version byte   (must equal EEPROM_VERSION)
//   2        2     Left minimum position
//   4        2     Left neutral position
//   6        2     Left maximum position
//   8        2     Right minimum position
//   10       2     Right neutral position
//   12       2     Right maximum position
//   14       2     Left max movement per update
//   16       2     Right max movement per update

const int EEPROM_ADDR_SIGNATURE       = 0;
const int EEPROM_ADDR_VERSION         = 1;
const int EEPROM_ADDR_LEFT_MIN        = 2;
const int EEPROM_ADDR_LEFT_NEUTRAL    = 4;
const int EEPROM_ADDR_LEFT_MAX        = 6;
const int EEPROM_ADDR_RIGHT_MIN       = 8;
const int EEPROM_ADDR_RIGHT_NEUTRAL   = 10;
const int EEPROM_ADDR_RIGHT_MAX       = 12;
const int EEPROM_ADDR_LEFT_MAX_MOVE   = 14;
const int EEPROM_ADDR_RIGHT_MAX_MOVE  = 16;

const byte EEPROM_SIGNATURE = 0x4D;  // 'M' for MAIRA — identifies this firmware
const byte EEPROM_VERSION   = 0x02;  // Increment this if the EEPROM layout changes

// --- Default calibration values ---
//
//   Applied when EEPROM data is missing, has the wrong signature/version,
//   or fails the range/ordering validity checks.

const int DEFAULT_LEFT_MIN      = 450;
const int DEFAULT_LEFT_NEUTRAL  = 600;
const int DEFAULT_LEFT_MAX      = 1350;
const int DEFAULT_RIGHT_MIN     = 450;
const int DEFAULT_RIGHT_NEUTRAL = 600;
const int DEFAULT_RIGHT_MAX     = 1350;

// --- Servo inversion configuration ---
//
//   ServoInversionMode_None  -> neither servo is inverted
//   ServoInversionMode_Left  -> left servo output is mirrored (0<->1800)
//   ServoInversionMode_Right -> right servo output is mirrored (0<->1800)
//
// Change the value below to match your physical motor mounting orientation.

enum ServoInversionMode {
  ServoInversionMode_None,
  ServoInversionMode_Left,
  ServoInversionMode_Right
};

const ServoInversionMode servoInversionMode = ServoInversionMode_Left;

// --- Servo objects ---

Servo leftServo;
Servo rightServo;

// --- Velocity limiter state (tenths-of-a-degree per motion update, 5-50) ---

int leftMaxMovementPerUpdate  = DEFAULT_MAX_MOVEMENT_PER_UPDATE;
int rightMaxMovementPerUpdate = DEFAULT_MAX_MOVEMENT_PER_UPDATE;

// --- Position state (all in tenths of a degree, 0-1800) ---

int leftMinPosition = DEFAULT_LEFT_MIN;
int rightMinPosition = DEFAULT_RIGHT_MIN;

int leftMaxPosition = DEFAULT_LEFT_MAX;
int rightMaxPosition = DEFAULT_RIGHT_MAX;

int leftNeutralPosition = DEFAULT_LEFT_NEUTRAL;
int rightNeutralPosition = DEFAULT_RIGHT_NEUTRAL;

int leftCurrentPosition = leftNeutralPosition;
int rightCurrentPosition = rightNeutralPosition;

int leftTargetPosition = leftNeutralPosition;
int rightTargetPosition = rightNeutralPosition;

// --- Timing state ---

unsigned long lastSetCommandTime = 0;
unsigned long lastMotionUpdateTime = 0;

// --- Serial input buffer ---

const int SERIAL_BUFFER_SIZE = 32;
char serialBuffer[SERIAL_BUFFER_SIZE];
int serialBufferIndex = 0;

// ===========================
// Helper: clamp a value to a range
// ===========================

int clampValue(int value, int minValue, int maxValue) {
  if (value < minValue) return minValue;
  if (value > maxValue) return maxValue;
  return value;
}

// ===========================
// Helper: convert tenths-of-a-degree (0-1800) to microseconds (500-2500)
// ===========================

int tenthsToMicroseconds(int tenths) {
  return (int)map((long)tenths, 0, 1800, PULSE_US_AT_0, PULSE_US_AT_1800);
}

// ===========================
// Helper: move a current position toward a target by at most maxStep
// ===========================

int moveToward(int currentValue, int targetValue, int maxStep) {
  int difference = targetValue - currentValue;

  if (difference > maxStep) {
    return currentValue + maxStep;
  }

  if (difference < -maxStep) {
    return currentValue - maxStep;
  }

  return targetValue;
}

// ===========================
// Helper: apply position inversion for a servo side if configured
// Inversion mirrors the position: 0 becomes 1800, 1800 becomes 0, 900 stays 900
// All other logic (min, max, neutral, target) stays in logical non-inverted space
// ===========================

int applyServoInversion(int logicalPositionTenths, bool isLeftServo) {
  bool shouldInvert = (servoInversionMode == ServoInversionMode_Left && isLeftServo) ||
                      (servoInversionMode == ServoInversionMode_Right && !isLeftServo);

  if (shouldInvert) {
    return 1800 - logicalPositionTenths;
  }

  return logicalPositionTenths;
}

// ===========================
// Helper: write both servos using current positions
// ===========================

void applyServoOutputs() {
  int leftMicroseconds = tenthsToMicroseconds(applyServoInversion(leftCurrentPosition, true));
  int rightMicroseconds = tenthsToMicroseconds(applyServoInversion(rightCurrentPosition, false));

  leftServo.writeMicroseconds(leftMicroseconds);
  rightServo.writeMicroseconds(rightMicroseconds);
}

// ===========================
// Helper: re-clamp neutral, current, and target positions to current min/max
// ===========================

void reclampAllPositions() {
  leftNeutralPosition = clampValue(leftNeutralPosition, leftMinPosition, leftMaxPosition);
  rightNeutralPosition = clampValue(rightNeutralPosition, rightMinPosition, rightMaxPosition);

  leftCurrentPosition = clampValue(leftCurrentPosition, leftMinPosition, leftMaxPosition);
  rightCurrentPosition = clampValue(rightCurrentPosition, rightMinPosition, rightMaxPosition);

  leftTargetPosition = clampValue(leftTargetPosition, leftMinPosition, leftMaxPosition);
  rightTargetPosition = clampValue(rightTargetPosition, rightMinPosition, rightMaxPosition);
}

// ===========================
// Helper: parse "LxxxxRyyyy" from a buffer starting at a given offset
// Returns true if parsing succeeded, with left/right values written to output params
// ===========================

bool parseLR(const char* command, int offset, int* outLeftValue, int* outRightValue) {
  // Expect: L at offset, then 4 digits, then R, then 4 digits
  int commandLength = strlen(command);

  if (commandLength < offset + 10) return false;
  if (command[offset] != 'L') return false;
  if (command[offset + 5] != 'R') return false;

  // Validate that all 8 digit positions are numeric
  for (int digitIndex = 0; digitIndex < 4; digitIndex++) {
    if (!isDigit(command[offset + 1 + digitIndex])) return false;
    if (!isDigit(command[offset + 6 + digitIndex])) return false;
  }

  // Parse the four-digit left value
  int leftValue = 0;
  for (int digitIndex = 0; digitIndex < 4; digitIndex++) {
    leftValue = leftValue * 10 + (command[offset + 1 + digitIndex] - '0');
  }

  // Parse the four-digit right value
  int rightValue = 0;
  for (int digitIndex = 0; digitIndex < 4; digitIndex++) {
    rightValue = rightValue * 10 + (command[offset + 6 + digitIndex] - '0');
  }

  *outLeftValue = leftValue;
  *outRightValue = rightValue;

  return true;
}

// ===========================
// Helper: read a 16-bit integer from EEPROM at the given address (little-endian)
// ===========================

int eepromReadInt16(int address) {
  byte lowByte  = EEPROM.read(address);
  byte highByte = EEPROM.read(address + 1);
  return (int)((unsigned int)highByte << 8 | (unsigned int)lowByte);
}

// ===========================
// Helper: write a 16-bit integer to EEPROM at the given address (little-endian)
// Uses EEPROM.update() to skip writes when the cell already holds the correct
// value, minimising EEPROM wear on the ATmega328P.
// ===========================

void eepromWriteInt16(int address, int value) {
  EEPROM.update(address,     (byte)((unsigned int)value & 0xFF));
  EEPROM.update(address + 1, (byte)(((unsigned int)value >> 8) & 0xFF));
}

// ===========================
// Helper: return true when the six calibration values satisfy all validity rules:
//   - every value is in the range 0..1800
//   - leftMin  <= leftNeutral  <= leftMax
//   - rightMin <= rightNeutral <= rightMax
// Any failure means the EEPROM data is corrupt or was written by a different
// firmware; defaults will be used instead.
// ===========================

bool isCalibrationValid(int leftMin, int leftNeutral, int leftMax,
                        int rightMin, int rightNeutral, int rightMax) {
  if (leftMin < 0      || leftMin > 1800)      return false;
  if (leftNeutral < 0  || leftNeutral > 1800)  return false;
  if (leftMax < 0      || leftMax > 1800)      return false;
  if (rightMin < 0     || rightMin > 1800)     return false;
  if (rightNeutral < 0 || rightNeutral > 1800) return false;
  if (rightMax < 0     || rightMax > 1800)     return false;

  if (leftMin > leftNeutral  || leftNeutral > leftMax)   return false;
  if (rightMin > rightNeutral || rightNeutral > rightMax) return false;

  return true;
}

// ===========================
// Helper: copy factory default values into the position-limit state
// ===========================

void restoreDefaultCalibration() {
  leftMinPosition      = DEFAULT_LEFT_MIN;
  leftNeutralPosition  = DEFAULT_LEFT_NEUTRAL;
  leftMaxPosition      = DEFAULT_LEFT_MAX;
  rightMinPosition     = DEFAULT_RIGHT_MIN;
  rightNeutralPosition = DEFAULT_RIGHT_NEUTRAL;
  rightMaxPosition     = DEFAULT_RIGHT_MAX;
}

// ===========================
// Helper: persist current calibration values to EEPROM
// Writes the signature/version markers first so a subsequent boot can
// confirm the data was written by this firmware version.
// EEPROM.update() is used throughout to avoid unnecessary write cycles.
// ===========================

void saveCalibrationToEEPROM() {
  EEPROM.update(EEPROM_ADDR_SIGNATURE, EEPROM_SIGNATURE);
  EEPROM.update(EEPROM_ADDR_VERSION,   EEPROM_VERSION);

  eepromWriteInt16(EEPROM_ADDR_LEFT_MIN,       leftMinPosition);
  eepromWriteInt16(EEPROM_ADDR_LEFT_NEUTRAL,   leftNeutralPosition);
  eepromWriteInt16(EEPROM_ADDR_LEFT_MAX,       leftMaxPosition);
  eepromWriteInt16(EEPROM_ADDR_RIGHT_MIN,      rightMinPosition);
  eepromWriteInt16(EEPROM_ADDR_RIGHT_NEUTRAL,  rightNeutralPosition);
  eepromWriteInt16(EEPROM_ADDR_RIGHT_MAX,      rightMaxPosition);
  eepromWriteInt16(EEPROM_ADDR_LEFT_MAX_MOVE,  leftMaxMovementPerUpdate);
  eepromWriteInt16(EEPROM_ADDR_RIGHT_MAX_MOVE, rightMaxMovementPerUpdate);
}

// ===========================
// Helper: load and validate calibration from EEPROM on startup
//
//   Step 1 — check signature and version.
//     If either byte does not match this firmware's expected constants the
//     EEPROM has never been written by this sketch (brand-new chip, or the
//     EEPROM layout changed with a firmware update).  Defaults are applied
//     and saved so the device self-heals.
//
//   Step 2 — read and validate the six stored values.
//     All six integers are read and passed through isCalibrationValid().
//     If validation fails (corrupt data, out-of-range, or ordering violated)
//     defaults are applied and saved.
//
//   Step 3 — apply valid data.
//     The loaded values are copied into the position-limit state variables
//     and reclampAllPositions() is called as a final defensive guard.
// ===========================

void loadCalibrationFromEEPROM() {
  byte storedSignature = EEPROM.read(EEPROM_ADDR_SIGNATURE);
  byte storedVersion   = EEPROM.read(EEPROM_ADDR_VERSION);

  if (storedSignature != EEPROM_SIGNATURE || storedVersion != EEPROM_VERSION) {
    // EEPROM has not been initialised by this firmware — apply and persist defaults
    restoreDefaultCalibration();
    saveCalibrationToEEPROM();
    return;
  }

  int loadedLeftMin      = eepromReadInt16(EEPROM_ADDR_LEFT_MIN);
  int loadedLeftNeutral  = eepromReadInt16(EEPROM_ADDR_LEFT_NEUTRAL);
  int loadedLeftMax      = eepromReadInt16(EEPROM_ADDR_LEFT_MAX);
  int loadedRightMin     = eepromReadInt16(EEPROM_ADDR_RIGHT_MIN);
  int loadedRightNeutral = eepromReadInt16(EEPROM_ADDR_RIGHT_NEUTRAL);
  int loadedRightMax     = eepromReadInt16(EEPROM_ADDR_RIGHT_MAX);
  int loadedLeftMaxMove  = eepromReadInt16(EEPROM_ADDR_LEFT_MAX_MOVE);
  int loadedRightMaxMove = eepromReadInt16(EEPROM_ADDR_RIGHT_MAX_MOVE);

  if (!isCalibrationValid(loadedLeftMin, loadedLeftNeutral, loadedLeftMax,
                           loadedRightMin, loadedRightNeutral, loadedRightMax)) {
    // Stored data failed validation — apply and persist defaults
    restoreDefaultCalibration();
    saveCalibrationToEEPROM();
    return;
  }

  // Data is valid — apply the loaded calibration values
  leftMinPosition      = loadedLeftMin;
  leftNeutralPosition  = loadedLeftNeutral;
  leftMaxPosition      = loadedLeftMax;
  rightMinPosition     = loadedRightMin;
  rightNeutralPosition = loadedRightNeutral;
  rightMaxPosition     = loadedRightMax;

  // Apply max movement values, falling back to defaults if out of range
  leftMaxMovementPerUpdate  = (loadedLeftMaxMove  >= 5 && loadedLeftMaxMove  <= 50) ? loadedLeftMaxMove  : DEFAULT_MAX_MOVEMENT_PER_UPDATE;
  rightMaxMovementPerUpdate = (loadedRightMaxMove >= 5 && loadedRightMaxMove <= 50) ? loadedRightMaxMove : DEFAULT_MAX_MOVEMENT_PER_UPDATE;

  // Defensive re-clamp after loading
  reclampAllPositions();
}

// ===========================
// Process a complete line received over serial
// ===========================

void processCommand(const char* command) {
  // --- Identity query ---
  if (strcmp(command, "WHAT ARE YOU?") == 0) {
    Serial.println("MAIRA SBT");
    return;
  }

  // All remaining commands are at least 11 characters: XLxxxxRyyyy
  int commandLength = strlen(command);

  if (commandLength < 11) return;

  char commandType = command[0];
  int leftParsedValue = 0;
  int rightParsedValue = 0;

  if (!parseLR(command, 1, &leftParsedValue, &rightParsedValue)) return;

  switch (commandType) {
    // --- S command: set target positions ---
    case 'S': {
      leftTargetPosition = clampValue(leftParsedValue, leftMinPosition, leftMaxPosition);
      rightTargetPosition = clampValue(rightParsedValue, rightMinPosition, rightMaxPosition);
      lastSetCommandTime = millis();
      break;
    }

    // --- N command: set neutral positions ---
    case 'N': {
      leftNeutralPosition = clampValue(leftParsedValue, leftMinPosition, leftMaxPosition);
      rightNeutralPosition = clampValue(rightParsedValue, rightMinPosition, rightMaxPosition);
      saveCalibrationToEEPROM();
      break;
    }

    // --- A command: set minimum positions ---
    case 'A': {
      leftMinPosition = clampValue(leftParsedValue, 0, 1800);
      rightMinPosition = clampValue(rightParsedValue, 0, 1800);
      reclampAllPositions();
      saveCalibrationToEEPROM();
      break;
    }

    // --- B command: set maximum positions ---
    case 'B': {
      leftMaxPosition = clampValue(leftParsedValue, 0, 1800);
      rightMaxPosition = clampValue(rightParsedValue, 0, 1800);
      reclampAllPositions();
      saveCalibrationToEEPROM();
      break;
    }

    // --- M command: set maximum movement per update (velocity limiter) ---
    case 'M': {
      leftMaxMovementPerUpdate  = clampValue(leftParsedValue,  5, 50);
      rightMaxMovementPerUpdate = clampValue(rightParsedValue, 5, 50);
      saveCalibrationToEEPROM();
      break;
    }

    default:
      // Unknown command type — ignore safely
      break;
  }
}

// ===========================
// Accumulate serial input and process complete lines
// Supports CR, LF, and CRLF line endings
// ===========================

void readSerialInput() {
  while (Serial.available() > 0) {
    char incomingByte = Serial.read();

    // Treat CR or LF as end-of-line
    if (incomingByte == '\r' || incomingByte == '\n') {
      if (serialBufferIndex > 0) {
        serialBuffer[serialBufferIndex] = '\0';
        processCommand(serialBuffer);
        serialBufferIndex = 0;
      }
      continue;
    }

    // Accumulate printable characters, guarding against overflow
    if (serialBufferIndex < SERIAL_BUFFER_SIZE - 1) {
      serialBuffer[serialBufferIndex] = incomingByte;
      serialBufferIndex++;
    } else {
      // Buffer overflow — discard the line
      serialBufferIndex = 0;
    }
  }
}

// ===========================
// Check for serial timeout and target neutral if needed
// ===========================

void checkSerialTimeout() {
  unsigned long elapsed = millis() - lastSetCommandTime;

  if (elapsed >= SERIAL_TIMEOUT_MS) {
    leftTargetPosition = leftNeutralPosition;
    rightTargetPosition = rightNeutralPosition;
  }
}

// ===========================
// Update the velocity-limited motion model
// ===========================

void updateMotion() {
  unsigned long now = millis();

  if (now - lastMotionUpdateTime < MOTION_UPDATE_INTERVAL_MS) return;

  lastMotionUpdateTime = now;

  leftCurrentPosition  = moveToward(leftCurrentPosition,  leftTargetPosition,  leftMaxMovementPerUpdate);
  rightCurrentPosition = moveToward(rightCurrentPosition, rightTargetPosition, rightMaxMovementPerUpdate);

  applyServoOutputs();
}

// ===========================
// Setup
// ===========================

void setup() {
  Serial.begin(SERIAL_BAUD_RATE);

  // Load calibration from EEPROM; falls back to defaults if missing or invalid
  loadCalibrationFromEEPROM();

  // Initialise current and target positions from the loaded neutral values
  leftCurrentPosition  = leftNeutralPosition;
  rightCurrentPosition = rightNeutralPosition;
  leftTargetPosition   = leftNeutralPosition;
  rightTargetPosition  = rightNeutralPosition;

  // Attach both servos
  leftServo.attach(LEFT_SERVO_PIN);
  rightServo.attach(RIGHT_SERVO_PIN);

  // Initialize timing so timeout-to-neutral is active from the start
  lastSetCommandTime = 0;
  lastMotionUpdateTime = millis();

  // Move both servos to neutral immediately
  applyServoOutputs();
}

// ===========================
// Main loop
// ===========================

void loop() {
  readSerialInput();
  checkSerialTimeout();
  updateMotion();
}
