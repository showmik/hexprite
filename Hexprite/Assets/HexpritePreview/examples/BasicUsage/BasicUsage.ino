/*
  BasicUsage.ino — HexpritePreview Library Example

  Demonstrates how to use the HexpritePreview library alongside your own
  display code. Your sketch runs normally; when Hexprite starts streaming
  live preview data, the library takes over the display. When streaming
  stops, your code resumes automatically.

  ─── QUICK START ───
  1. Install the HexpritePreview library (copy to Arduino/libraries/)
     PlatformIO users: copy the library folder to your project's lib/ — see PLATFORMIO.md
  2. Set your display constructor and serial baud below
  3. Upload to your board
  4. In Hexprite, enable Hardware Preview and select your serial port

  Your original sketch is NEVER overwritten — just add the three lines
  marked with "// ← HexpritePreview" and you're done.
*/

#include <Arduino.h>
#include <U8g2lib.h>
#include <Wire.h>
#include <HexpritePreview.h>  // ← HexpritePreview (1/3)

// ═══════════════════════════════════════════════════════════════════════════════
//  YOUR DISPLAY SETUP — edit to match your hardware
// ═══════════════════════════════════════════════════════════════════════════════

// Uncomment the constructor that matches your display:
U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
// U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
// U8G2_SH1106_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);

#define SERIAL_BAUD 115200
#define SERIAL_RX_BUFFER 8192

// ═══════════════════════════════════════════════════════════════════════════════
//  HEXPRITE PREVIEW + YOUR CODE
// ═══════════════════════════════════════════════════════════════════════════════

HexpritePreview preview;  // ← HexpritePreview (2/3)

int counter = 0;

void setup() {
#if defined(ESP32)
    // Custom ESP32 boards may receive preview data on Serial0 (UART USB chip).
    Serial.setRxBufferSize(SERIAL_RX_BUFFER);
    Serial.begin(SERIAL_BAUD);
    Serial0.setRxBufferSize(SERIAL_RX_BUFFER);
    Serial0.begin(SERIAL_BAUD);
#else
    Serial.setRxBufferSize(SERIAL_RX_BUFFER);
    Serial.begin(SERIAL_BAUD);
#endif

    u8g2.begin();

#if defined(ESP32)
    preview.begin(u8g2, Serial, &Serial0);  // ← HexpritePreview (3/3)
#else
    preview.begin(u8g2, Serial);          // ← HexpritePreview (3/3)
#endif
    preview.setTimeout(3000);
}

void loop() {
    // ── Hexprite Preview Check ──────────────────────────────────────────
    if (preview.update()) return;
    if (preview.isActive()) return;

    // Optional: skip local UI while a packet is still arriving
    if (preview.hasPendingSerial()) return;

    // ── Your Normal Display Code ────────────────────────────────────────
    u8g2.clearBuffer();
    u8g2.setFont(u8g2_font_6x10_tf);
    u8g2.drawStr(0, 10, "My Project");

    char buf[32];
    snprintf(buf, sizeof(buf), "Count: %d", counter++);
    u8g2.drawStr(0, 30, buf);

    u8g2.drawStr(0, 55, "Hexprite ready");

    u8g2.sendBuffer();
    delay(100);
}
