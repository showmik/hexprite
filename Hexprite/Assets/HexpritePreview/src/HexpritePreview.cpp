/*
  HexpritePreview.cpp — Implementation
  See HexpritePreview.h for usage.
*/

#include "HexpritePreview.h"

#if defined(ESP32) || defined(ESP8266)
static uint8_t s_packetBuffer[HEXPRITE_MAX_BUFFER];
#endif

HexpritePreview::HexpritePreview()
    : _display(nullptr)
    , _serialPrimary(nullptr)
    , _serialAlt(nullptr)
    , _lockedSerial(nullptr)
    , _buffer(nullptr)
    , _bufferSize(HEXPRITE_MAX_BUFFER)
    , _bufferIndex(0)
    , _packetStarted(false)
    , _lastFrameTime(0)
    , _timeoutMs(3000)
    , _active(false)
    , _bufferReady(false)
    , _bytesReceived(0)
    , _bytesReceivedPrimary(0)
    , _bytesReceivedAlt(0)
    , _framesDisplayed(0)
    , _checksumFails(0)
    , _invalidPackets(0)
{
}

HexpritePreview::~HexpritePreview()
{
#if !(defined(ESP32) || defined(ESP8266))
    if (_buffer) {
        free(_buffer);
        _buffer = nullptr;
    }
#endif
}

void HexpritePreview::begin(U8G2& display, Stream& serial, Stream* serialAlt)
{
    _display = &display;
    _serialPrimary = &serial;
    _serialAlt = serialAlt;

#if defined(ESP32) || defined(ESP8266)
    _buffer = s_packetBuffer;
    _bufferReady = true;
#else
    if (_buffer) free(_buffer);
    _buffer = (uint8_t*)malloc(_bufferSize);
    if (!_buffer && _bufferSize > 1040) {
        _bufferSize = 1040;
        _buffer = (uint8_t*)malloc(_bufferSize);
    }
    _bufferReady = (_buffer != nullptr);
#endif

    resetPacket();
    _active = false;
    _bytesReceived = 0;
    _bytesReceivedPrimary = 0;
    _bytesReceivedAlt = 0;
    _framesDisplayed = 0;
    _checksumFails = 0;
    _invalidPackets = 0;
}

void HexpritePreview::setTimeout(unsigned long timeoutMs)
{
    _timeoutMs = timeoutMs;
}

void HexpritePreview::resetPacket()
{
    _bufferIndex = 0;
    _packetStarted = false;
    _lockedSerial = nullptr;
}

bool HexpritePreview::isActive() const
{
    if (!_active) return false;
    return (millis() - _lastFrameTime) < _timeoutMs;
}

bool HexpritePreview::isReceiving() const
{
    return _packetStarted;
}

bool HexpritePreview::hasPendingSerial() const
{
    if (_packetStarted) return true;
    if (_serialPrimary && _serialPrimary->available() > 0) return true;
    if (_serialAlt && _serialAlt->available() > 0) return true;
    return false;
}

HexpritePreviewStats HexpritePreview::getStats() const
{
    char port = '-';
    if (_packetStarted || _active) {
        if (_lockedSerial == _serialPrimary) port = 'P';
        else if (_lockedSerial == _serialAlt) port = 'A';
    }

    return {
        _bytesReceived,
        _bytesReceivedPrimary,
        _bytesReceivedAlt,
        _framesDisplayed,
        _checksumFails,
        _invalidPackets,
        _packetStarted,
        isActive(),
        _bufferReady,
        port
    };
}

void HexpritePreview::processByte(uint8_t b, Stream& source)
{
    if (!_bufferReady || !_buffer) return;

    _bytesReceived++;
    if (&source == _serialPrimary) {
        _bytesReceivedPrimary++;
    } else if (_serialAlt && &source == _serialAlt) {
        _bytesReceivedAlt++;
    }

    if (!_packetStarted) {
        if (b == 0xAA) {
            _buffer[0] = b;
            _bufferIndex = 1;
            _packetStarted = true;
            _lockedSerial = &source;
        }
        return;
    }

    if (_lockedSerial != &source) {
        return;
    }

    if (_bufferIndex == 1 && b != 0x55) {
        resetPacket();
        if (b == 0xAA) {
            _buffer[0] = b;
            _bufferIndex = 1;
            _packetStarted = true;
            _lockedSerial = &source;
        }
        return;
    }

    if (_bufferIndex >= _bufferSize) {
        _invalidPackets++;
        resetPacket();
        return;
    }

    _buffer[_bufferIndex++] = b;

    if (_bufferIndex < 6) {
        return;
    }

    uint16_t w = (uint16_t)(_buffer[2] | (_buffer[3] << 8));
    uint16_t h = (uint16_t)(_buffer[4] | (_buffer[5] << 8));
    uint32_t dataSize = ((uint32_t)(w + 7) / 8) * (uint32_t)h;
    uint32_t totalSize = 6 + dataSize + 1;

    if (totalSize > (uint32_t)_bufferSize || w == 0 || h == 0) {
        _invalidPackets++;
        resetPacket();
        return;
    }

    if ((uint32_t)_bufferIndex != totalSize) {
        return;
    }

    uint8_t checksum = 0;
    for (uint32_t i = 0; i < totalSize - 1; i++) {
        checksum ^= _buffer[i];
    }

    if (checksum == _buffer[totalSize - 1]) {
        _display->clearBuffer();
        _display->drawXBM(0, 0, w, h, &_buffer[6]);
        _display->sendBuffer();

        _lastFrameTime = millis();
        _active = true;
        _framesDisplayed++;

        source.write((uint8_t)HEXPRITE_ACK_BYTE);
        source.flush();
    } else {
        _checksumFails++;
        source.write((uint8_t)HEXPRITE_ERR_CHECKSUM);
        source.flush();
    }

    resetPacket();
}

bool HexpritePreview::update()
{
    if (!_display || !_bufferReady || !_buffer) return false;

    if (_active && (millis() - _lastFrameTime) >= _timeoutMs) {
        _active = false;
    }

    bool frameDisplayedBefore = _framesDisplayed;

    if (_packetStarted && _lockedSerial) {
        Stream& locked = *_lockedSerial;
        while (locked.available()) {
            processByte((uint8_t)locked.read(), locked);
        }
    } else {
        if (_serialPrimary) {
            while (_serialPrimary->available()) {
                processByte((uint8_t)_serialPrimary->read(), *_serialPrimary);
            }
        }
        if (_serialAlt) {
            while (_serialAlt->available()) {
                processByte((uint8_t)_serialAlt->read(), *_serialAlt);
            }
        }
    }

    return _framesDisplayed > frameDisplayedBefore;
}
