using System;
using System.Collections.Generic;

namespace Hexprite.Core
{
    public sealed class WirePinMapping
    {
        public string DisplayPin { get; set; } = string.Empty;
        public string BoardPin { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string WireColor { get; set; } = string.Empty; // e.g. "Red", "Black", "Yellow", "Blue"
    }

    public sealed class HardwarePreviewWiringConfig
    {
        public const string DefaultBoard = "ESP32 DevKit";
        public const string DefaultInterface = "I2C";
        public const string DefaultSdaPin = "21";
        public const string DefaultSclPin = "22";
        public const string DefaultI2cAddress = "0x3C";
        public const string DefaultDisplayModel = "SSD1306 128x64";

        public string BoardPreset { get; set; } = DefaultBoard;
        public string InterfaceType { get; set; } = DefaultInterface; // "I2C" or "SPI"
        public string SdaPin { get; set; } = DefaultSdaPin;
        public string SclPin { get; set; } = DefaultSclPin;
        public string I2cAddress { get; set; } = DefaultI2cAddress;
        public bool UseSoftwareI2c { get; set; }

        public string DisplayModel { get; set; } = DefaultDisplayModel;

        // SPI pin configurations (defaults match default board: ESP32 DevKit)
        public string CsPin { get; set; } = "5";
        public string DcPin { get; set; } = "16";
        public string RstPin { get; set; } = "17";
        public string ClkPin { get; set; } = "18";
        public string MosiPin { get; set; } = "23";

        public int BaudRate { get; set; } = 115200;

        public HardwarePreviewWiringConfig Clone()
        {
            return new HardwarePreviewWiringConfig
            {
                BoardPreset = BoardPreset,
                InterfaceType = InterfaceType,
                SdaPin = SdaPin,
                SclPin = SclPin,
                I2cAddress = I2cAddress,
                UseSoftwareI2c = UseSoftwareI2c,
                DisplayModel = DisplayModel,
                CsPin = CsPin,
                DcPin = DcPin,
                RstPin = RstPin,
                ClkPin = ClkPin,
                MosiPin = MosiPin,
                BaudRate = BaudRate,
            };
        }

        public static List<string> GetBoardPresets() =>
        [
            "ESP32 DevKit",
            "Arduino Uno / Nano",
            "Arduino Mega",
            "Raspberry Pi Pico (RP2040)",
            "ESP8266 NodeMCU / D1 Mini",
            "STM32 Blue Pill",
            "Custom Board"
        ];

        public static List<string> GetDisplayModels() =>
        [
            "SSD1306 128x64",
            "SSD1306 128x32",
            "SH1106 128x64",
            "SSD1309 128x64",
            "ST7920 128x64"
        ];

        public static (int Width, int Height) GetDisplayDimensions(string? displayModel)
        {
            if (string.IsNullOrEmpty(displayModel)) return (128, 64);
            if (displayModel.Contains("128x32", StringComparison.OrdinalIgnoreCase)) return (128, 32);
            if (displayModel.Contains("128x64", StringComparison.OrdinalIgnoreCase)) return (128, 64);
            if (displayModel.Contains("128x128", StringComparison.OrdinalIgnoreCase)) return (128, 128);
            if (displayModel.Contains("64x48", StringComparison.OrdinalIgnoreCase)) return (64, 48);
            if (displayModel.Contains("64x32", StringComparison.OrdinalIgnoreCase)) return (64, 32);
            return (128, 64);
        }

        public static int GetMaxBufferSize(string boardPreset)
        {
            return boardPreset switch
            {
                "ESP32 DevKit" or "Raspberry Pi Pico (RP2040)" or "ESP8266 NodeMCU / D1 Mini" or "STM32 Blue Pill" => 32800,
                "Arduino Mega" => 8200,
                "Arduino Uno / Nano" => 1040,
                _ => 4200
            };
        }

        public void ApplyPreset(string presetName)
        {
            BoardPreset = presetName;
            switch (presetName)
            {
                case "ESP32 DevKit":
                    InterfaceType = "I2C";
                    SdaPin = "21";
                    SclPin = "22";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "5";
                    DcPin = "16";
                    RstPin = "17";
                    ClkPin = "18";
                    MosiPin = "23";
                    break;

                case "Arduino Uno / Nano":
                    InterfaceType = "I2C";
                    SdaPin = "A4";
                    SclPin = "A5";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "10";
                    DcPin = "9";
                    RstPin = "8";
                    ClkPin = "13";
                    MosiPin = "11";
                    break;

                case "Arduino Mega":
                    InterfaceType = "I2C";
                    SdaPin = "20";
                    SclPin = "21";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "53";
                    DcPin = "9";
                    RstPin = "8";
                    ClkPin = "52";
                    MosiPin = "51";
                    break;

                case "Raspberry Pi Pico (RP2040)":
                    InterfaceType = "I2C";
                    SdaPin = "GP4";
                    SclPin = "GP5";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "GP17";
                    DcPin = "GP16";
                    RstPin = "GP20";
                    ClkPin = "GP18";
                    MosiPin = "GP19";
                    break;

                case "ESP8266 NodeMCU / D1 Mini":
                    InterfaceType = "I2C";
                    SdaPin = "D2";
                    SclPin = "D1";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "D8";
                    DcPin = "D2";
                    RstPin = "D1";
                    ClkPin = "D5";
                    MosiPin = "D7";
                    break;

                case "STM32 Blue Pill":
                    InterfaceType = "I2C";
                    SdaPin = "PB7";
                    SclPin = "PB6";
                    I2cAddress = "0x3C";
                    UseSoftwareI2c = false;
                    CsPin = "PA4";
                    DcPin = "PA3";
                    RstPin = "PA2";
                    ClkPin = "PA5";
                    MosiPin = "PA7";
                    break;

                default:
                    // Custom Board: preserve current pins
                    break;
            }
        }

        public static string NormalizePin(string? pin)
        {
            if (string.IsNullOrWhiteSpace(pin)) return string.Empty;
            return pin.Trim().ToUpperInvariant();
        }

        public static string NormalizeI2cAddress(string? addr)
        {
            if (string.IsNullOrWhiteSpace(addr)) return DefaultI2cAddress;
            string trimmed = addr.Trim();
            if (!trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "0x" + trimmed;
            }
            return "0x" + trimmed[2..].ToUpperInvariant();
        }

        public (bool IsValid, string? Warning, string? Error) Validate()
        {
            if (string.IsNullOrWhiteSpace(DisplayModel))
            {
                return (false, null, "Please select a display model.");
            }

            if (InterfaceType == "I2C")
            {
                if (string.Equals(DisplayModel?.Trim(), "ST7920 128x64", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, null, "ST7920 128x64 does not support I2C. Please select SPI interface.");
                }

                if (string.IsNullOrWhiteSpace(SdaPin))
                    return (false, null, "SDA pin cannot be empty.");
                if (string.IsNullOrWhiteSpace(SclPin))
                    return (false, null, "SCL pin cannot be empty.");

                if (string.Equals(SdaPin.Trim(), SclPin.Trim(), StringComparison.OrdinalIgnoreCase))
                    return (false, null, "SDA and SCL cannot be the same pin.");

                string addr = I2cAddress.Trim();
                if (!addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || addr.Length < 3 || addr.Length > 4 ||
                    !byte.TryParse(addr[2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return (false, null, "I2C address must be a valid hex format (e.g. 0x3C or 0x3D).");

                // Check AVR hardware I2C constraint
                if (BoardPreset.Contains("Arduino Uno", StringComparison.OrdinalIgnoreCase) ||
                    BoardPreset.Contains("Nano", StringComparison.OrdinalIgnoreCase))
                {
                    bool isDefaultAvr = (string.Equals(SdaPin.Trim(), "A4", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(SdaPin.Trim(), "SDA", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(SdaPin.Trim(), "18", StringComparison.OrdinalIgnoreCase)) &&
                                        (string.Equals(SclPin.Trim(), "A5", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(SclPin.Trim(), "SCL", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(SclPin.Trim(), "19", StringComparison.OrdinalIgnoreCase));

                    if (!isDefaultAvr && !UseSoftwareI2c)
                    {
                        return (true, "AVR hardware I2C is hardwired to A4/A5. Enable Software I2C to use custom pins on Uno/Nano.", null);
                    }
                }

                if (BoardPreset.Contains("Mega", StringComparison.OrdinalIgnoreCase))
                {
                    bool isDefaultMega = (string.Equals(SdaPin.Trim(), "20", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(SdaPin.Trim(), "SDA", StringComparison.OrdinalIgnoreCase)) &&
                                         (string.Equals(SclPin.Trim(), "21", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(SclPin.Trim(), "SCL", StringComparison.OrdinalIgnoreCase));

                    if (!isDefaultMega && !UseSoftwareI2c)
                    {
                        return (true, "Arduino Mega hardware I2C is hardwired to 20/21. Enable Software I2C to use custom pins.", null);
                    }
                }
            }
            else if (InterfaceType == "SPI")
            {
                if (string.IsNullOrWhiteSpace(CsPin)) return (false, null, "CS pin cannot be empty.");
                if (string.IsNullOrWhiteSpace(DcPin)) return (false, null, "DC pin cannot be empty.");
                if (string.IsNullOrWhiteSpace(ClkPin)) return (false, null, "Clock (SCK) pin cannot be empty.");
                if (string.IsNullOrWhiteSpace(MosiPin)) return (false, null, "MOSI pin cannot be empty.");

                var spiPins = new List<(string Name, string Pin)>
                {
                    ("CS", CsPin.Trim()),
                    ("DC", DcPin.Trim()),
                    ("Clock (SCK)", ClkPin.Trim()),
                    ("MOSI", MosiPin.Trim())
                };
                if (!string.IsNullOrWhiteSpace(RstPin) && !string.Equals(RstPin.Trim(), "U8X8_PIN_NONE", StringComparison.OrdinalIgnoreCase))
                {
                    spiPins.Add(("RST", RstPin.Trim()));
                }

                var duplicates = spiPins.GroupBy(p => p.Pin, StringComparer.OrdinalIgnoreCase)
                                        .Where(g => g.Count() > 1)
                                        .ToList();
                if (duplicates.Count > 0)
                {
                    var dup = duplicates[0];
                    string names = string.Join(" and ", dup.Select(x => x.Name));
                    return (false, null, $"SPI pins must be unique: {names} cannot share pin '{dup.Key}'.");
                }

                if (BoardPreset.Contains("ESP8266", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(DcPin.Trim(), "D3", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(DcPin.Trim(), "0", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, "D3 (GPIO 0) is a boot mode pin. If held LOW at power-on, ESP8266 enters flash mode.", null);
                    }
                }
            }

            return (true, null, null);
        }
    }
}
