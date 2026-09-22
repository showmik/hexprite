/*
 * ═══════════════════════════════════════════════════════════════════════════════
 *  config.h — Hexprite Live Hardware Preview Configuration
 * ═══════════════════════════════════════════════════════════════════════════════
 *
 *  Edit this file to match your hardware setup:
 *   1. Set your serial baud rate
 *   2. Choose I2C or SPI connection
 *   3. Set your pin numbers
 *   4. Uncomment your display type
 *
 *  Then build & upload from PlatformIO.
 */

#pragma once

// ─── Serial baud rate (must match Hexprite sidebar setting) ────────────────
#define SERIAL_BAUD 115200

// ─── Display connection type: uncomment ONE ────────────────────────────────
#define USE_I2C
// #define USE_SPI

// ═══════════════════════════════════════════════════════════════════════════════
//  I2C CONFIGURATION (only used when USE_I2C is defined)
// ═══════════════════════════════════════════════════════════════════════════════
//
//  Common default pins by board:
//    Arduino Uno/Nano  →  SDA = A4,  SCL = A5
//    Arduino Mega      →  SDA = 20,  SCL = 21
//    ESP32 DevKit      →  SDA = 21,  SCL = 22
//    ESP8266 NodeMCU   →  SDA = D2 (4),  SCL = D1 (5)
//    STM32 Blue Pill   →  SDA = PB7, SCL = PB6
//    RP2040 Pico       →  SDA = GP4, SCL = GP5

#define I2C_SDA_PIN   SDA       // Change to your SDA pin number if needed
#define I2C_SCL_PIN   SCL       // Change to your SCL pin number if needed
#define I2C_ADDRESS   0x3C      // Most OLEDs use 0x3C, some use 0x3D

// ═══════════════════════════════════════════════════════════════════════════════
//  SPI CONFIGURATION (only used when USE_SPI is defined)
// ═══════════════════════════════════════════════════════════════════════════════
//
//  Common default pins by board:
//    Arduino Uno/Nano  →  CLK = 13, MOSI = 11, CS = 10, DC = 9, RST = 8
//    ESP32 DevKit      →  CLK = 18, MOSI = 23, CS = 5,  DC = 16, RST = 17
//    ESP8266 NodeMCU   →  CLK = D5 (14), MOSI = D7 (13), CS = D8 (15)

#define SPI_CS_PIN    10        // Chip Select
#define SPI_DC_PIN    9         // Data/Command
#define SPI_RST_PIN   8         // Reset (set to U8X8_PIN_NONE if not wired)
#define SPI_CLK_PIN   13        // Clock (SCK)
#define SPI_MOSI_PIN  11        // MOSI (SDA/DIN)

// ═══════════════════════════════════════════════════════════════════════════════
//  DISPLAY TYPE — uncomment ONE that matches your hardware
// ═══════════════════════════════════════════════════════════════════════════════

// ── I2C displays ────────────────────────────────────────────────────────────
#define DISPLAY_SSD1306_128X64_I2C           // ★ Most common — SSD1306 128×64 I2C
// #define DISPLAY_SSD1306_128X32_I2C        // SSD1306 128×32 I2C (half-height)
// #define DISPLAY_SH1106_128X64_I2C         // SH1106 128×64 I2C (common alternative)
// #define DISPLAY_SSD1309_128X64_I2C        // SSD1309 128×64 I2C

// ── SPI displays ────────────────────────────────────────────────────────────
// #define DISPLAY_SSD1306_128X64_SPI        // SSD1306 128×64 SPI
// #define DISPLAY_SSD1306_128X32_SPI        // SSD1306 128×32 SPI
// #define DISPLAY_SH1106_128X64_SPI         // SH1106 128×64 SPI
// #define DISPLAY_SSD1309_128X64_SPI        // SSD1309 128×64 SPI
// #define DISPLAY_ST7920_128X64_SPI         // ST7920 128×64 SPI (e.g. RepRap LCDs)
