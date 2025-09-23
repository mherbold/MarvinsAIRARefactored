// Arduino Nano (ATmega328P)
// - 25 kHz PWM on D9 (OC1A) / D10 (OC1B), TOP=320 -> duty 0..320
// - Handshake: "WHAT ARE YOU?" -> "MAIRA WIND"
// - Commands:  L### / R###
// - LED ACK blink on valid L/R
// - Safety idle: 5 s without L/R -> both 0%, and RPM streaming pauses
// - Dual tach: D2 (INT0)=Left, D3 (INT1)=Right, each with 10k pull-up to 5V
// - Streams (when not idle): "RPM L=<left> R=<right>" every 500 ms

#include <Arduino.h>

// -------- PWM (Timer1 @ 25 kHz) --------
const uint8_t  PIN_LEFT        = 9;      // OC1A
const uint8_t  PIN_RIGHT       = 10;     // OC1B
const uint16_t PWM_TOP         = 320;    // 16e6 / (2 * 1 * 320) = 25 kHz

// -------- LED ACK (non-blocking) --------
const uint8_t  LED_PIN         = LED_BUILTIN;  // D13
const unsigned long LED_MS     = 50;
bool            ledIsOn        = false;
unsigned long   ledOffAtMs     = 0;

// -------- Inactivity watchdog --------
const unsigned long INACTIVITY_MS = 2000; // 5 s
unsigned long lastLRCommandMs     = 0;
bool          idleZeroApplied     = true; // start in "idle" (0%)

// -------- Tachometer (RPM) --------
const uint8_t  TACH_L_PIN         = 2;   // D2 = INT0
const uint8_t  TACH_R_PIN         = 3;   // D3 = INT1
const uint8_t  PULSES_PER_REV     = 2;   // typical PC fan
volatile uint32_t tachLPulses     = 0;
volatile uint32_t tachRPulses     = 0;
volatile uint32_t lastLPulseUs    = 0;
volatile uint32_t lastRPulseUs    = 0;

// -------- RPM streaming schedule --------
const unsigned long RPM_REPORT_MS = 500;
unsigned long nextRpmReportMs = 0;
unsigned long lastReportMs    = 0;

void tachLISR() {
  uint32_t now = micros();
  if (now - lastLPulseUs > 100) {  // ~100 us deglitch
    tachLPulses++;
    lastLPulseUs = now;
  }
}
void tachRISR() {
  uint32_t now = micros();
  if (now - lastRPulseUs > 100) {
    tachRPulses++;
    lastRPulseUs = now;
  }
}

static bool parseValue0toTOP(const String& digits, uint16_t& out) {
  if (digits.length() == 0 || digits.length() > 3) return false; // 0..320 fits 3 digits
  for (size_t i = 0; i < digits.length(); ++i) if (!isDigit(digits[i])) return false;
  long v = digits.toInt();
  if (v < 0) v = 0;
  if (v > PWM_TOP) v = PWM_TOP;  // clamp; change to "return false" to hard-reject
  out = (uint16_t)v;
  return true;
}

void setupPwm25kHzTimer1() {
  TCCR1A = 0; TCCR1B = 0; TCNT1 = 0;
  // Phase-correct PWM, TOP=ICR1 (WGM13:0 = 0b1000 → WGM13=1, WGM11=1)
  TCCR1A = _BV(COM1A1) | _BV(COM1B1) | _BV(WGM11); // non-inverting A/B
  TCCR1B = _BV(WGM13)  | _BV(CS10);                // no prescaler
  ICR1   = PWM_TOP;
  OCR1A  = 0; OCR1B = 0;
  pinMode(PIN_LEFT, OUTPUT);
  pinMode(PIN_RIGHT, OUTPUT);
}

inline void triggerAckBlink() {
  digitalWrite(LED_PIN, HIGH);
  ledIsOn    = true;
  ledOffAtMs = millis() + LED_MS;
}

static uint32_t rpmFromPulses(uint32_t pulses, unsigned long elapsedMs) {
  if (elapsedMs == 0) elapsedMs = 1;
  // RPM = (pulses / PPR) * (60000 / elapsedMs)
  uint32_t rpm = (pulses * 60000UL) / PULSES_PER_REV;
  rpm /= elapsedMs;
  return rpm;
}

void setup() {
  setupPwm25kHzTimer1();

  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);

  // Tach inputs (use external 10k pull-ups to 5V on each tach if available)
  pinMode(TACH_L_PIN, INPUT_PULLUP);
  pinMode(TACH_R_PIN, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(TACH_L_PIN), tachLISR, FALLING);
  attachInterrupt(digitalPinToInterrupt(TACH_R_PIN), tachRISR, FALLING);

  Serial.begin(115200);
  Serial.setTimeout(5);

  unsigned long now = millis();
  lastLRCommandMs = now;
  lastReportMs    = now;
  nextRpmReportMs = now + RPM_REPORT_MS;
}

void loop() {
  // --- Serial handling ---
  if (Serial.available() > 0) {
    String line = Serial.readStringUntil('\n');
    line.trim();
    if (line.length() > 0) {
      if (line.equalsIgnoreCase("WHAT ARE YOU?")) {
        Serial.println("MAIRA WIND");
      } else {
        String cmd = line; cmd.toUpperCase();
        if ((cmd[0] == 'L' || cmd[0] == 'R') && cmd.length() >= 2) {
          String digits = cmd.substring(1); digits.trim();
          uint16_t val;
          if (parseValue0toTOP(digits, val)) {
            if (cmd[0] == 'L') OCR1A = val; else OCR1B = val;

            // Activity → exit idle and reset RPM timing/counters
            unsigned long now = millis();
            lastLRCommandMs = now;
            idleZeroApplied = false;
            lastReportMs    = now;
            nextRpmReportMs = now + RPM_REPORT_MS;
            noInterrupts(); tachLPulses = 0; tachRPulses = 0; interrupts();

            triggerAckBlink(); // ACK
          }
          // silently ignore malformed commands
        }
      }
    }
  }

  // --- LED auto-off ---
  if (ledIsOn && millis() >= ledOffAtMs) {
    digitalWrite(LED_PIN, LOW);
    ledIsOn = false;
  }

  // --- inactivity watchdog ---
  if (!idleZeroApplied && (millis() - lastLRCommandMs >= INACTIVITY_MS)) {
    OCR1A = 0; OCR1B = 0;
    idleZeroApplied = true;

    // Pause streaming cleanly: reset RPM timing/counters
    unsigned long now = millis();
    lastReportMs    = now;
    nextRpmReportMs = now + RPM_REPORT_MS;
    noInterrupts(); tachLPulses = 0; tachRPulses = 0; interrupts();

    // optional: triggerAckBlink();
  }

  // --- continuous RPM streaming (only when not idle) ---
  unsigned long now = millis();
  if ((long)(now - nextRpmReportMs) >= 0) {
    // Schedule next tick first (catch up if late)
    do { nextRpmReportMs += RPM_REPORT_MS; } while ((long)(now - nextRpmReportMs) >= 0);

    if (!idleZeroApplied) {
      unsigned long elapsed = now - lastReportMs;
      lastReportMs = now;

      uint32_t pulsesL, pulsesR;
      noInterrupts();
      pulsesL = tachLPulses; tachLPulses = 0;
      pulsesR = tachRPulses; tachRPulses = 0;
      interrupts();

      uint32_t rpmL = rpmFromPulses(pulsesL, elapsed);
      uint32_t rpmR = rpmFromPulses(pulsesR, elapsed);

      // Single labeled line:
      Serial.print("RPM L=");
      Serial.print(rpmL);
      Serial.print(" R=");
      Serial.println(rpmR);
    } else {
      // While idle, keep time base fresh and counters empty
      lastReportMs = now;
      noInterrupts(); tachLPulses = 0; tachRPulses = 0; interrupts();
    }
  }
}
