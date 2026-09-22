/*
  HexpritePreview-Standalone-Arduino.ino
  Standalone companion sketch for Hexprite Live Hardware Preview.
  
  ⚠ IMPORTANT: This sketch REPLACES your project code on the board.
  For a better approach that preserves your code, use the HexpritePreview
  Arduino library instead — see the HexpritePreview/ folder.
  Just add 3 lines to your existing sketch and you're done.
  
  Streams live canvas data from Hexprite (Desktop) to a physical display.
  Requires the U8g2 library by oliver.
  
  Protocol v2: [Header: 0xAA, 0x55] [Width_L, Width_H] [Height_L, Height_H] [Data...] [Checksum: XOR]
  Width and Height are 2-byte little-endian values to support canvases > 255px.
  On a valid, displayed frame this sketch writes a single ACK byte (0xA5) back to
  Hexprite so it can confirm the board is alive and receiving.

  ─── QUICK START ───
  1. Set your DISPLAY, PINS, and SERIAL_BAUD below
  2. Upload to your board
  3. In Hexprite, enable Hardware Preview and select your serial port
*/

#include <Arduino.h>
#include <U8g2lib.h>
#include <Wire.h>

// ═══════════════════════════════════════════════════════════════════════════════
//  USER CONFIGURATION — Edit these to match your setup
// ═══════════════════════════════════════════════════════════════════════════════

// --- Serial baud rate (must match Hexprite settings) ---
#define SERIAL_BAUD 115200

// --- Display connection type: uncomment ONE ---
#define USE_I2C
// #define USE_SOFTWARE_I2C   // Uncomment for custom I2C pins on AVR (Uno/Nano)
// #define USE_SPI

// --- I2C Pin Configuration (only used when USE_I2C or USE_SOFTWARE_I2C is defined) ---
#define I2C_SDA_PIN   SDA     // Default SDA pin (Arduino Uno: A4, ESP32: 21, etc.)
#define I2C_SCL_PIN   SCL     // Default SCL pin (Arduino Uno: A5, ESP32: 22, etc.)
#define I2C_ADDRESS   0x3C    // Most OLED modules use 0x3C, some use 0x3D

// --- SPI Pin Configuration (only used when USE_SPI is defined) ---
#define SPI_CS_PIN    10      // Chip Select
#define SPI_DC_PIN    9       // Data/Command
#define SPI_RST_PIN   8       // Reset (set to U8X8_PIN_NONE if not wired)
#define SPI_CLK_PIN   13      // Clock (SCK) — usually hardware SPI
#define SPI_MOSI_PIN  11      // MOSI (SDA/DIN) — usually hardware SPI

// --- Display type: uncomment ONE ---
// Each line shows: [Display Controller] [Resolution] [Connection]
// I2C displays:
#define DISPLAY_SSD1306_128X64_I2C           // Most common — SSD1306 128×64 I2C
// #define DISPLAY_SSD1306_128X32_I2C        // SSD1306 128×32 I2C
// #define DISPLAY_SH1106_128X64_I2C         // SH1106 128×64 I2C
// #define DISPLAY_SSD1309_128X64_I2C        // SSD1309 128×64 I2C

// SPI displays:
// #define DISPLAY_SSD1306_128X64_SPI        // SSD1306 128×64 SPI
// #define DISPLAY_SSD1306_128X32_SPI        // SSD1306 128×32 SPI
// #define DISPLAY_SH1106_128X64_SPI         // SH1106 128×64 SPI
// #define DISPLAY_SSD1309_128X64_SPI        // SSD1309 128×64 SPI
// #define DISPLAY_ST7920_128X64_SPI         // ST7920 128×64 SPI (e.g. RepRap displays)

// ═══════════════════════════════════════════════════════════════════════════════
//  DISPLAY CONSTRUCTOR — auto-selected from your config above
// ═══════════════════════════════════════════════════════════════════════════════

// --- I2C displays ---
#if defined(USE_SOFTWARE_I2C)
  #if defined(DISPLAY_SSD1306_128X64_I2C)
    U8G2_SSD1306_128X64_NONAME_F_SW_I2C u8g2(U8G2_R0, /* clock=*/ I2C_SCL_PIN, /* data=*/ I2C_SDA_PIN, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SSD1306_128X32_I2C)
    U8G2_SSD1306_128X32_UNIVISION_F_SW_I2C u8g2(U8G2_R0, /* clock=*/ I2C_SCL_PIN, /* data=*/ I2C_SDA_PIN, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SH1106_128X64_I2C)
    U8G2_SH1106_128X64_NONAME_F_SW_I2C u8g2(U8G2_R0, /* clock=*/ I2C_SCL_PIN, /* data=*/ I2C_SDA_PIN, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SSD1309_128X64_I2C)
    U8G2_SSD1309_128X64_NONAME0_F_SW_I2C u8g2(U8G2_R0, /* clock=*/ I2C_SCL_PIN, /* data=*/ I2C_SDA_PIN, /* reset=*/ U8X8_PIN_NONE);
  #endif
#else
  #if defined(DISPLAY_SSD1306_128X64_I2C)
    U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SSD1306_128X32_I2C)
    U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SH1106_128X64_I2C)
    U8G2_SH1106_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
  #elif defined(DISPLAY_SSD1309_128X64_I2C)
    U8G2_SSD1309_128X64_NONAME0_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);
  #endif
#endif

// --- SPI displays ---
#elif defined(DISPLAY_SSD1306_128X64_SPI)
  U8G2_SSD1306_128X64_NONAME_F_4W_HW_SPI u8g2(U8G2_R0, SPI_CS_PIN, SPI_DC_PIN, SPI_RST_PIN);
#elif defined(DISPLAY_SSD1306_128X32_SPI)
  U8G2_SSD1306_128X32_UNIVISION_F_4W_HW_SPI u8g2(U8G2_R0, SPI_CS_PIN, SPI_DC_PIN, SPI_RST_PIN);
#elif defined(DISPLAY_SH1106_128X64_SPI)
  U8G2_SH1106_128X64_NONAME_F_4W_HW_SPI u8g2(U8G2_R0, SPI_CS_PIN, SPI_DC_PIN, SPI_RST_PIN);
#elif defined(DISPLAY_SSD1309_128X64_SPI)
  U8G2_SSD1309_128X64_NONAME0_F_4W_HW_SPI u8g2(U8G2_R0, SPI_CS_PIN, SPI_DC_PIN, SPI_RST_PIN);
#elif defined(DISPLAY_ST7920_128X64_SPI)
  U8G2_ST7920_128X64_F_HW_SPI u8g2(U8G2_R0, SPI_CS_PIN, SPI_RST_PIN);

#else
  #error "No display type selected! Uncomment one DISPLAY_* define above."
#endif

// ═══════════════════════════════════════════════════════════════════════════════
//  PROTOCOL HANDLER — do not edit below this line
// ═══════════════════════════════════════════════════════════════════════════════

// Platform-aware buffer size:
// Arduino Uno/Nano (2 KB RAM):   1040 → fits 128×64 / 128×32
// Arduino Mega    (8 KB RAM):    8200 → max ~512×128
// ESP32/ESP8266/RP2040/STM32:   32800 → max 512×512
#if defined(ESP32) || defined(ESP8266) || defined(ARDUINO_ARCH_RP2040) || defined(ARDUINO_ARCH_STM32)
  #define MAX_BUFFER 32800
#elif defined(__AVR_ATmega2560__)
  #define MAX_BUFFER 8200
#elif defined(__AVR_ATmega328P__) || defined(__AVR_ATmega168__) || defined(ARDUINO_AVR_UNO) || defined(ARDUINO_AVR_NANO)
  #define MAX_BUFFER 1040
#else
  #define MAX_BUFFER 4200
#endif
byte buffer[MAX_BUFFER];
int bufferIndex = 0;
bool packetStarted = false;

// Diagnostic response codes sent back to Hexprite
#define HEXPRITE_ACK_OK         0xA5  // Frame received and displayed
#define HEXPRITE_ERR_I2C_NACK   0xE1  // Display not responding on I2C (check SDA/SCL pins & address)
#define HEXPRITE_ERR_CHECKSUM   0xE2  // Checksum mismatch

void setup() {
  Serial.begin(SERIAL_BAUD);

  #if defined(USE_I2C) && !defined(USE_SOFTWARE_I2C)
    #if defined(__AVR__)
      #if (I2C_SDA_PIN != SDA || I2C_SCL_PIN != SCL)
        #error "AVR hardware I2C pins are hardwired to A4 (SDA) and A5 (SCL). To use other pins, switch to a software I2C constructor (_SW_I2C)."
      #endif
      Wire.begin();
    #elif defined(ESP32) || defined(ESP8266)
      Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN);
    #elif defined(ARDUINO_ARCH_RP2040) || defined(ARDUINO_ARCH_STM32)
      Wire.setSDA(I2C_SDA_PIN);
      Wire.setSCL(I2C_SCL_PIN);
      Wire.begin();
    #else
      Wire.begin();
    #endif

    #if defined(WIRE_HAS_TIMEOUT)
      Wire.setWireTimeout(25000, true); // 25ms timeout, reset bus on hang
    #endif
  #endif

  u8g2.begin();
  
  // Show a waiting screen so the user knows the board is ready
  u8g2.clearBuffer();
  u8g2.setFont(u8g2_font_6x10_tf);
  u8g2.drawStr(0, 10, "Hexprite Live");
  u8g2.drawStr(0, 25, "Waiting for data...");
  u8g2.sendBuffer();
}

void loop() {
  while (Serial.available()) {
    byte b = Serial.read();
    
    if (!packetStarted) {
      if (b == 0xAA) {
        // Potential header start
        buffer[0] = b;
        bufferIndex = 1;
        packetStarted = true;
      }
    } else {
      if (bufferIndex == 1 && b != 0x55) {
        // Not the second byte of header, reset and check if this byte starts a new packet
        packetStarted = (b == 0xAA);
        bufferIndex = packetStarted ? 1 : 0;
        if (packetStarted) buffer[0] = 0xAA;
        continue;
      }
      
      buffer[bufferIndex++] = b;
      
      // We need at least 6 bytes to know the payload size
      // Header(2) + Width_L(1) + Width_H(1) + Height_L(1) + Height_H(1)
      if (bufferIndex >= 6) {
        uint16_t w = (uint16_t)(buffer[2] | (buffer[3] << 8));  // Little-endian 16-bit width
        uint16_t h = (uint16_t)(buffer[4] | (buffer[5] << 8));  // Little-endian 16-bit height
        uint32_t dataSize = ((uint32_t)(w + 7) / 8) * (uint32_t)h;
        uint32_t totalSize = 6 + dataSize + 1; // Header(2) + W(2) + H(2) + Data + Checksum(1)
        
        if (totalSize > (uint32_t)MAX_BUFFER || w == 0 || h == 0) {
          // Packet too large or invalid dimensions, safety reset
          packetStarted = false;
          bufferIndex = 0;
          continue;
        }
        
        if ((uint32_t)bufferIndex == totalSize) {
          // Full packet received, verify checksum
          byte checksum = 0;
          for (uint32_t i = 0; i < totalSize - 1; i++) {
            checksum ^= buffer[i];
          }
          
          if (checksum == buffer[totalSize - 1]) {
            #if defined(USE_I2C) && !defined(USE_SOFTWARE_I2C)
            // Verify display is responding on I2C before acknowledging
            Wire.beginTransmission(I2C_ADDRESS);
            if (Wire.endTransmission() != 0) {
              // Display not found on I2C bus (wrong SDA/SCL pins, bad wiring, or wrong address)
              Serial.write((uint8_t)HEXPRITE_ERR_I2C_NACK);
              Serial.flush();
              packetStarted = false;
              bufferIndex = 0;
              continue;
            }
            #endif

            // Checksum and I2C valid, update display
            u8g2.clearBuffer();
            // Draw the XBM data (Hexprite sends LSB-first XBM format)
            u8g2.drawXBM(0, 0, w, h, &buffer[6]);
            u8g2.sendBuffer();

            // ACK: tell Hexprite this frame was displayed
            Serial.write((uint8_t)HEXPRITE_ACK_OK);
            Serial.flush();
          } else {
            Serial.write((uint8_t)HEXPRITE_ERR_CHECKSUM);
            Serial.flush();
          }
          
          packetStarted = false;
          bufferIndex = 0;
        }
      }
    }
  }
}
