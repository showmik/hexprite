/*
  HexpritePreview.h — Live Hardware Preview Library for Hexprite

  Add this library to your own Arduino sketch so you can use Hexprite's
  live preview feature WITHOUT overwriting your project code.

  Usage:
    #include <HexpritePreview.h>

    HexpritePreview preview;

    void setup() {
        Serial.begin(115200);
        u8g2.begin();
        preview.begin(u8g2, Serial);
    }

    void loop() {
        if (preview.update()) return;   // Hexprite is streaming — skip your code
        if (preview.isActive()) return; // Still active between frames

        // ... your normal display code ...
    }

  ESP32 custom boards with a USB-UART chip may need dual serial:
    Serial0.setRxBufferSize(8192);
    Serial0.begin(115200);
    preview.begin(u8g2, Serial, &Serial0);

  Protocol v2: [0xAA 0x55] [W_L W_H] [H_L H_H] [XBM Data...] [XOR Checksum]
  On a valid, displayed frame the board writes a single ACK byte (0xA5) back
  on the same stream the packet arrived on. Older firmware without this line
  simply never sends it — Hexprite treats "no ACK yet" as normal, not an error.
*/

#ifndef HEXPRITE_PREVIEW_H
#define HEXPRITE_PREVIEW_H

#include <Arduino.h>
#include <U8g2lib.h>

// Platform-aware receive buffer size.
// Must match or exceed (6 + ceil(W/8)*H + 1) for the largest canvas you use.
// Arduino Uno/Nano (2 KB RAM):   1040 → fits 128×64 / 128×32
// Arduino Mega    (8 KB RAM):    8200 → max ~512×128
// ESP32/ESP8266/RP2040/STM32:   32800 → max 512×512
#if defined(ESP32) || defined(ESP8266) || defined(ARDUINO_ARCH_RP2040) || defined(ARDUINO_ARCH_STM32)
  #define HEXPRITE_MAX_BUFFER 32800
#elif defined(__AVR_ATmega2560__)
  #define HEXPRITE_MAX_BUFFER 8200
#elif defined(__AVR_ATmega328P__) || defined(__AVR_ATmega168__) || defined(ARDUINO_AVR_UNO) || defined(ARDUINO_AVR_NANO)
  #define HEXPRITE_MAX_BUFFER 1040
#else
  #define HEXPRITE_MAX_BUFFER 4200
#endif

// Sent back to the host after a frame is successfully checksummed and displayed.
#define HEXPRITE_ACK_BYTE     0xA5
#define HEXPRITE_ACK_OK       0xA5
#define HEXPRITE_ERR_I2C_NACK 0xE1
#define HEXPRITE_ERR_CHECKSUM 0xE2

struct HexpritePreviewStats {
    uint32_t bytesReceived;
    uint32_t bytesReceivedPrimary;
    uint32_t bytesReceivedAlt;
    uint32_t framesDisplayed;
    uint32_t checksumFails;
    uint32_t invalidPackets;
    bool receiving;
    bool active;
    bool bufferReady;
    char activePort; // 'P' primary, 'A' alt, '-' idle
};

class HexpritePreview {
public:
    HexpritePreview();
    ~HexpritePreview();

    /// Initialize with a U8g2 display and serial stream(s).
    /// Call in setup() after Serial.begin() and u8g2.begin().
    /// @param display    Your U8G2 display object.
    /// @param serial     Primary serial stream (default: Serial).
    /// @param serialAlt  Optional second stream (e.g. &Serial0 on ESP32 UART boards).
    void begin(U8G2& display, Stream& serial = Serial, Stream* serialAlt = nullptr);

    /// Check for incoming preview data. Call at the top of loop().
    /// @return true if a preview frame was received and displayed this cycle.
    bool update();

    /// @return true if preview data was received within the timeout window.
    bool isActive() const;

    /// @return true while a preview packet is partially received.
    bool isReceiving() const;

    /// @return true if serial data is waiting or a packet is in progress.
    bool hasPendingSerial() const;

    /// Set the inactivity timeout in milliseconds (default: 3000).
    void setTimeout(unsigned long timeoutMs);

    /// Diagnostic counters — useful to see which serial port receives data.
    HexpritePreviewStats getStats() const;

private:
    void resetPacket();
    void processByte(uint8_t b, Stream& source);

    U8G2* _display;
    Stream* _serialPrimary;
    Stream* _serialAlt;
    Stream* _lockedSerial;
    uint8_t* _buffer;
    int _bufferSize;
    int _bufferIndex;
    bool _packetStarted;
    unsigned long _lastFrameTime;
    unsigned long _timeoutMs;
    bool _active;
    bool _bufferReady;

    uint32_t _bytesReceived;
    uint32_t _bytesReceivedPrimary;
    uint32_t _bytesReceivedAlt;
    uint32_t _framesDisplayed;
    uint32_t _checksumFails;
    uint32_t _invalidPackets;
};

#endif // HEXPRITE_PREVIEW_H
