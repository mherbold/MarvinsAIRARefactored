// Arduino Nano (ATmega328P)
//
// - 25 kHz PWM on D9 (OC1A) / D10 (OC1B), TOP=320 -> duty 0..320
// - Handshake: "WHAT ARE YOU?" -> "MAIRA WIND"
// - Command:   "L<x>R<y>"   where x,y are 0..320 (left/right duty)
// - Response:  "L<a>R<b>"   where a,b are RPM for left/right
// - LED ACK blink on valid LR command
// - Safety idle: 2 s without LR -> both 0%
// - Dual tach: D2 (INT0)=Left, D3 (INT1)=Right, with inline 10k series resistor
// - RPM measurement: time for 10 tach pulses per fan (non-blocking)

#include <Arduino.h>

// -------- PWM (Timer1 @ 25 kHz) --------
const uint8_t       PIN_LEFT         = 10;          // OC1A
const uint8_t       PIN_RIGHT        = 9;           // OC1B
const uint16_t      PWM_TOP          = 320;         // 16e6 / (2 * 1 * 320) = 25 kHz

// -------- LED ACK (non-blocking) --------
const uint8_t       LED_PIN          = LED_BUILTIN; // D13
const unsigned long LED_MS           = 50;
bool                ledIsOn          = false;
unsigned long       ledOffAtMs       = 0;

// -------- Inactivity watchdog --------
const unsigned long INACTIVITY_MS    = 2000;        // 2 s
unsigned long       lastLRCommandMs  = 0;
bool                idleZeroApplied  = true;        // start in idle (0%)

// -------- Tachometer / RPM measurement --------
const uint8_t       TACH_L_PIN       = 3;           // D3 = INT0
const uint8_t       TACH_R_PIN       = 2;           // D2 = INT1
const uint8_t       PULSES_PER_REV   = 2;           // typical PC fan
const uint8_t       MEAS_PULSES      = 50;          // pulses per measurement window
const uint32_t      STALL_TIMEOUT_US = 500000UL;    // 0.5 s without pulses -> treat as 0 RPM
const uint32_t      DEGLITCH_US      = 100UL;       // ignore pulses closer than this

// -------- Left tach state --------
volatile uint8_t    tachLCount       = 0;
volatile uint32_t   tachLStartUs     = 0;
volatile uint32_t   lastLPulseUs     = 0;           // last valid pulse time for deglitch + stall

// -------- Right tach state --------
volatile uint8_t    tachRCount       = 0;
volatile uint32_t   tachRStartUs     = 0;
volatile uint32_t   lastRPulseUs     = 0;

// -------- Last computed RPM values (used if no fresh window yet) --------
uint32_t            rpmLeftLast      = 0;
uint32_t            rpmRightLast     = 0;

// -------- Helpers --------

// Clamp & parse 0..PWM_TOP from a decimal substring
static bool parseValue0toTOP( const String & digits, uint16_t & out )
{
  if ( digits.length() == 0 || digits.length() > 3 )
  {
    return false; // 0..320 fits 3 digits
  }

  for ( size_t i = 0; i < (size_t) digits.length(); ++i )
  {
    if ( !isDigit( digits[ i ] ) )
    {
      return false;
    }
  }

  long v = digits.toInt();

  if ( v < 0 )
  {
    v = 0;
  }

  if ( v > PWM_TOP )
  {
    v = PWM_TOP;
  }

  out = (uint16_t) v;

  return true;
}

// Parse "LxxxRyyy" (x,y 0..PWM_TOP) into left/right values.
// Accepts optional whitespace, case-insensitive. Returns true on success.
static bool parseLRCommand( const String & line, uint16_t & left, uint16_t & right )
{
  String cmd = line;

  cmd.trim();
  cmd.toUpperCase();

  int lPos = cmd.indexOf( 'L' );
  int rPos = cmd.indexOf( 'R' );

  if ( lPos < 0 || rPos < 0 )
  {
    return false;
  }

  if ( rPos <= lPos )
  {
    return false; // expect L before R
  }

  String lDigits = cmd.substring( lPos + 1, rPos );
  String rDigits = cmd.substring( rPos + 1 );

  lDigits.trim();
  rDigits.trim();

  uint16_t lVal, rVal;

  if ( !parseValue0toTOP( lDigits, lVal ) )
  {
     return false;
  }

  if ( !parseValue0toTOP( rDigits, rVal ) )
  {
    return false;
  }

  left = lVal;
  right = rVal;

  return true;
}

// Timer1 @ 25 kHz phase-correct PWM on OC1A/OC1B
void setupPwm25kHzTimer1()
{
  TCCR1A = 0;
  TCCR1B = 0;
  TCNT1  = 0;

  // Phase-correct PWM, TOP=ICR1 (WGM13:0 = 0b1000 → WGM13=1, WGM11=1)
  TCCR1A = _BV( COM1A1 ) | _BV( COM1B1 ) | _BV( WGM11 ); // non-inverting A/B
  TCCR1B = _BV( WGM13 )  | _BV( CS10 ); // WGM13=1, no prescaler
  ICR1   = PWM_TOP;
  OCR1A  = 0;
  OCR1B  = 0;

  pinMode( PIN_LEFT, OUTPUT );
  pinMode( PIN_RIGHT, OUTPUT );
}

inline void triggerAckBlink()
{
  digitalWrite( LED_PIN, HIGH );

  ledIsOn = true;
  ledOffAtMs = millis() + LED_MS;
}

// Compute RPM from a completed MEAS_PULSES window
static uint32_t rpmFromWindow( uint32_t startUs, uint32_t endUs )
{
  if ( endUs <= startUs )
  {
    return 0;
  }

  uint32_t dtMicros = endUs - startUs;

  if ( dtMicros == 0 )
  {
     return 0;
  }

  // RPM = (Pulses * 60s/min) / (PPR * dt_s)
  // Pulses = MEAS_PULSES, dt_s = dtMicros / 1e6
  // => RPM = (MEAS_PULSES * 60000000) / (PPR * dtMicros)

  uint32_t rpm = (uint32_t) MEAS_PULSES * 60000000UL;

  rpm /= (uint32_t) PULSES_PER_REV;
  rpm /= dtMicros;

  return rpm;
}

// -------- Tach ISRs --------
// NOTE: With a PC fan tach (open-collector to GND) plus your inline 10k resistor, using INPUT_PULLUP and FALLING edge is correct.
void tachLISR()
{
  uint32_t nowUs = micros();

  if ( nowUs - lastLPulseUs <= DEGLITCH_US )
  {
    return; // ignore glitches
  }

  lastLPulseUs = nowUs;

  if ( tachLCount == 0 )
  {
    tachLStartUs = nowUs;
    tachLCount = 1;
  }
  else
  {
    tachLCount++;

    if (tachLCount >= MEAS_PULSES)
    {
      tachLCount = 0;

      rpmLeftLast = rpmFromWindow( tachLStartUs, nowUs );
    }
  }
}

void tachRISR()
{
  uint32_t nowUs = micros();

  if ( nowUs - lastRPulseUs <= DEGLITCH_US )
  {
    return; // ignore glitches
  }

  lastRPulseUs = nowUs;

  if ( tachRCount == 0 )
  {
    tachRStartUs = nowUs;
    tachRCount = 1;
  }
  else
  {
    tachRCount++;

    if ( tachRCount >= MEAS_PULSES )
    {
      tachRCount = 0;

      rpmRightLast = rpmFromWindow( tachRStartUs, nowUs );
    }
  }
}

void setup()
{
  setupPwm25kHzTimer1();

  pinMode( LED_PIN, OUTPUT );
  digitalWrite( LED_PIN, LOW );

  // Tach inputs (open-collector tach to GND, Arduino supplies pull-up).
  // Your inline 10k resistor between fan tach and Nano pin is fine.

  pinMode( TACH_L_PIN, INPUT_PULLUP );
  pinMode( TACH_R_PIN, INPUT_PULLUP );

  attachInterrupt( digitalPinToInterrupt( TACH_L_PIN ), tachLISR, FALLING );
  attachInterrupt( digitalPinToInterrupt( TACH_R_PIN ), tachRISR, FALLING );

  Serial.begin( 115200 );
  Serial.setTimeout( 5 );

  unsigned long now = millis();

  lastLRCommandMs = now;
  idleZeroApplied = true;
}

void loop()
{
  // --- Serial handling ---
  if ( Serial.available() > 0 )
  {
    String line = Serial.readStringUntil( '\n' );

    line.trim();

    if ( line.length() > 0 )
    {
      if ( line.equalsIgnoreCase( "WHAT ARE YOU?" ) )
      {
        Serial.println( "MAIRA WIND" );
      }
      else
      {
        uint16_t leftVal, rightVal;

        if ( parseLRCommand( line, leftVal, rightVal ) )
        {
          // Apply PWM
          OCR1A = leftVal;
          OCR1B = rightVal;

          // Mark activity
          unsigned long nowMs = millis();

          lastLRCommandMs = nowMs;
          idleZeroApplied = false;

          // Respond as "LaRb" (no spaces)
          Serial.print( 'L' );
          Serial.print( rpmLeftLast );
          Serial.print( 'R' );
          Serial.println( rpmRightLast );

          triggerAckBlink(); // ACK on valid command
        }

        // silently ignore malformed commands
      }
    }
  }

  // --- LED auto-off ---
  if ( ledIsOn && millis() >= ledOffAtMs )
  {
    digitalWrite( LED_PIN, LOW );

    ledIsOn = false;
  }

  // --- inactivity watchdog ---
  if ( !idleZeroApplied && ( millis() - lastLRCommandMs >= INACTIVITY_MS ) )
  {
    OCR1A = 0;
    OCR1B = 0;

    idleZeroApplied = true;
  }
}
