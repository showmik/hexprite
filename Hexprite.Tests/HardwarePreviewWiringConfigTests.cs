using System;
using System.IO;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("HardwarePreviewPreferences")]
    [Trait("Category", "Unit")]
    public class HardwarePreviewWiringConfigTests
    {
        [Fact]
        public void PresetSelection_UnoNano_SetsA4AndA5()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Arduino Uno / Nano");

            Assert.Equal("A4", config.SdaPin);
            Assert.Equal("A5", config.SclPin);
            Assert.Equal("0x3C", config.I2cAddress);
            Assert.Equal("I2C", config.InterfaceType);
            Assert.False(config.UseSoftwareI2c);
        }

        [Fact]
        public void PresetSelection_ESP32_Sets21And22()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("ESP32 DevKit");

            Assert.Equal("21", config.SdaPin);
            Assert.Equal("22", config.SclPin);
            Assert.Equal("0x3C", config.I2cAddress);
            Assert.Equal("I2C", config.InterfaceType);
        }

        [Fact]
        public void PresetSelection_RP2040_SetsGP4AndGP5()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Raspberry Pi Pico (RP2040)");

            Assert.Equal("GP4", config.SdaPin);
            Assert.Equal("GP5", config.SclPin);
            Assert.Equal("0x3C", config.I2cAddress);
        }

        [Fact]
        public void Validation_AvrCustomPinsWithoutSoftwareI2c_ReturnsWarning()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Arduino Uno / Nano");
            config.SdaPin = "2";
            config.SclPin = "3";
            config.UseSoftwareI2c = false;

            var result = config.Validate();

            Assert.True(result.IsValid);
            Assert.NotNull(result.Warning);
            Assert.Contains("Software I2C", result.Warning);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Validation_AvrCustomPinsWithSoftwareI2c_NoWarning()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Arduino Uno / Nano");
            config.SdaPin = "2";
            config.SclPin = "3";
            config.UseSoftwareI2c = true;

            var result = config.Validate();

            Assert.True(result.IsValid);
            Assert.Null(result.Warning);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Validation_SameSdaAndScl_ReturnsError()
        {
            var config = new HardwarePreviewWiringConfig
            {
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "21"
            };

            var result = config.Validate();

            Assert.False(result.IsValid);
            Assert.NotNull(result.Error);
            Assert.Contains("same pin", result.Error);
        }

        [Fact]
        public void Validation_InvalidI2cAddress_ReturnsError()
        {
            var config = new HardwarePreviewWiringConfig
            {
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22",
                I2cAddress = "invalid"
            };

            var result = config.Validate();

            Assert.False(result.IsValid);
            Assert.NotNull(result.Error);
            Assert.Contains("hex format", result.Error);
        }

        [Fact]
        public void SketchGenerator_HardwareI2c_GeneratesExpectedDirectives()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22",
                I2cAddress = "0x3C",
                DisplayModel = "SSD1306 128x64",
                UseSoftwareI2c = false
            };

            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);

            Assert.Contains("#define SERIAL_BAUD 115200", sketch);
            Assert.Contains("#define USE_I2C", sketch);
            Assert.Contains("#define I2C_SDA_PIN   21", sketch);
            Assert.Contains("#define I2C_SCL_PIN   22", sketch);
            Assert.Contains("#define I2C_ADDRESS   0x3C", sketch);
            Assert.Contains("U8G2_SSD1306_128X64_NONAME_F_HW_I2C", sketch);
            Assert.Contains("Wire.begin(I2C_SDA_PIN, I2C_SCL_PIN);", sketch);
            Assert.Contains("HEXPRITE_ACK_OK", sketch);
            Assert.Contains("HEXPRITE_ERR_I2C_NACK", sketch);
        }

        [Fact]
        public void SketchGenerator_SoftwareI2c_GeneratesSwConstructor()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                InterfaceType = "I2C",
                SdaPin = "4",
                SclPin = "5",
                I2cAddress = "0x3C",
                DisplayModel = "SSD1306 128x64",
                UseSoftwareI2c = true
            };

            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);

            Assert.Contains("#define USE_SOFTWARE_I2C", sketch);
            Assert.Contains("U8G2_SSD1306_128X64_NONAME_F_SW_I2C u8g2(U8G2_R0, /* clock=*/ 5, /* data=*/ 4", sketch);
        }

        [Fact]
        public void SketchGenerator_SPI_GeneratesSpiDirectivesAndConstructor()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "SPI",
                CsPin = "15",
                DcPin = "4",
                RstPin = "16",
                ClkPin = "18",
                MosiPin = "23",
                DisplayModel = "SSD1306 128x64"
            };

            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);

            Assert.Contains("#define USE_SPI", sketch);
            Assert.Contains("#define SPI_CS_PIN    15", sketch);
            Assert.Contains("#define SPI_DC_PIN    4", sketch);
            Assert.Contains("#define SPI_RST_PIN   16", sketch);
            Assert.Contains("U8G2_SSD1306_128X64_NONAME_F_4W_HW_SPI u8g2(U8G2_R0, /* cs=*/ 15, /* dc=*/ 4, /* reset=*/ 16);", sketch);
        }

        [Fact]
        public void LibrarySnippet_GeneratesExpectedIntegrationLines()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22"
            };

            string snippet = HardwarePreviewSketchGenerator.GenerateLibrarySnippet(config);

            Assert.Contains("#include <U8g2lib.h>", snippet);
            Assert.Contains("#include <HexpritePreview.h>", snippet);
            Assert.Contains("Wire.begin(21, 22);", snippet);
            Assert.Contains("HexpritePreview hexpritePreview;", snippet);
            Assert.Contains("hexpritePreview.begin(u8g2);", snippet);
            Assert.Contains("hexpritePreview.update();", snippet);
        }

        [Fact]
        public void WireMapping_ReturnsCorrectPins()
        {
            var config = new HardwarePreviewWiringConfig
            {
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22"
            };

            var mappings = HardwarePreviewSketchGenerator.GetWireMapping(config);

            Assert.Contains(mappings, m => m.DisplayPin == "VCC");
            Assert.Contains(mappings, m => m.DisplayPin == "GND");
            Assert.Contains(mappings, m => m.DisplayPin == "SDA / D1" && m.BoardPin == "21");
            Assert.Contains(mappings, m => m.DisplayPin == "SCL / D0" && m.BoardPin == "22");
        }

        [Fact]
        public void GetMaxBufferSize_ReturnsExpectedSizesForPresets()
        {
            Assert.Equal(1040, HardwarePreviewWiringConfig.GetMaxBufferSize("Arduino Uno / Nano"));
            Assert.Equal(8200, HardwarePreviewWiringConfig.GetMaxBufferSize("Arduino Mega"));
            Assert.Equal(32800, HardwarePreviewWiringConfig.GetMaxBufferSize("ESP32 DevKit"));
            Assert.Equal(32800, HardwarePreviewWiringConfig.GetMaxBufferSize("Raspberry Pi Pico (RP2040)"));
            Assert.Equal(32800, HardwarePreviewWiringConfig.GetMaxBufferSize("ESP8266 NodeMCU / D1 Mini"));
            Assert.Equal(32800, HardwarePreviewWiringConfig.GetMaxBufferSize("STM32 Blue Pill"));
            Assert.Equal(4200, HardwarePreviewWiringConfig.GetMaxBufferSize("Custom Board"));
            Assert.Equal(4200, HardwarePreviewWiringConfig.GetMaxBufferSize("Unknown Board"));
        }

        [Fact]
        public void PresetSelection_AllBoards_SetsCorrectDefaultPins()
        {
            var config = new HardwarePreviewWiringConfig();

            // 1. ESP32
            config.ApplyPreset("ESP32 DevKit");
            Assert.Equal("21", config.SdaPin);
            Assert.Equal("22", config.SclPin);
            Assert.Equal("5", config.CsPin);
            Assert.Equal("16", config.DcPin);
            Assert.Equal("17", config.RstPin);
            Assert.Equal("18", config.ClkPin);
            Assert.Equal("23", config.MosiPin);

            // 2. Arduino Uno / Nano
            config.ApplyPreset("Arduino Uno / Nano");
            Assert.Equal("A4", config.SdaPin);
            Assert.Equal("A5", config.SclPin);
            Assert.Equal("10", config.CsPin);
            Assert.Equal("9", config.DcPin);
            Assert.Equal("8", config.RstPin);
            Assert.Equal("13", config.ClkPin);
            Assert.Equal("11", config.MosiPin);

            // 3. Arduino Mega
            config.ApplyPreset("Arduino Mega");
            Assert.Equal("20", config.SdaPin);
            Assert.Equal("21", config.SclPin);
            Assert.Equal("53", config.CsPin);
            Assert.Equal("9", config.DcPin);
            Assert.Equal("8", config.RstPin);
            Assert.Equal("52", config.ClkPin);
            Assert.Equal("51", config.MosiPin);

            // 4. RP2040
            config.ApplyPreset("Raspberry Pi Pico (RP2040)");
            Assert.Equal("GP4", config.SdaPin);
            Assert.Equal("GP5", config.SclPin);
            Assert.Equal("GP17", config.CsPin);
            Assert.Equal("GP16", config.DcPin);
            Assert.Equal("GP20", config.RstPin);
            Assert.Equal("GP18", config.ClkPin);
            Assert.Equal("GP19", config.MosiPin);

            // 5. ESP8266
            config.ApplyPreset("ESP8266 NodeMCU / D1 Mini");
            Assert.Equal("D2", config.SdaPin);
            Assert.Equal("D1", config.SclPin);
            Assert.Equal("D8", config.CsPin);
            Assert.Equal("D2", config.DcPin);
            Assert.Equal("D1", config.RstPin);
            Assert.Equal("D5", config.ClkPin);
            Assert.Equal("D7", config.MosiPin);

            // 6. STM32 Blue Pill
            config.ApplyPreset("STM32 Blue Pill");
            Assert.Equal("PB7", config.SdaPin);
            Assert.Equal("PB6", config.SclPin);
            Assert.Equal("PA4", config.CsPin);
            Assert.Equal("PA3", config.DcPin);
            Assert.Equal("PA2", config.RstPin);
            Assert.Equal("PA5", config.ClkPin);
            Assert.Equal("PA7", config.MosiPin);
        }

        [Fact]
        public void Validation_ESP8266_D3Pin_ReturnsBootStrappingWarning()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP8266 NodeMCU / D1 Mini",
                InterfaceType = "SPI",
                CsPin = "D8",
                DcPin = "D3", // GPIO 0
                RstPin = "D1",
                ClkPin = "D5",
                MosiPin = "D7"
            };

            var result = config.Validate();
            Assert.True(result.IsValid);
            Assert.NotNull(result.Warning);
            Assert.Contains("boot mode", result.Warning);
        }

        [Fact]
        public void GenerateArduinoSketch_RP2040_EmitsSetSDAAndSetSCL()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Raspberry Pi Pico (RP2040)",
                InterfaceType = "I2C",
                SdaPin = "GP4",
                SclPin = "GP5"
            };

            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);
            Assert.Contains("Wire.setSDA(I2C_SDA_PIN);", sketch);
            Assert.Contains("Wire.setSCL(I2C_SCL_PIN);", sketch);
            Assert.Contains("Wire.begin();", sketch);
        }

        [Fact]
        public void GenerateLibrarySnippet_RP2040_EmitsSetSDAAndSetSCL()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Raspberry Pi Pico (RP2040)",
                InterfaceType = "I2C",
                SdaPin = "GP4",
                SclPin = "GP5"
            };

            string snippet = HardwarePreviewSketchGenerator.GenerateLibrarySnippet(config);
            Assert.Contains("Wire.setSDA(GP4);", snippet);
            Assert.Contains("Wire.setSCL(GP5);", snippet);
            Assert.Contains("Wire.begin();", snippet);
        }

        [Fact]
        public void UserPreferences_RoundTripsWiringSettings()
        {
            // Save custom settings
            UserPreferencesService.Update(p =>
            {
                p.HardwarePreviewBoardPreset = "Raspberry Pi Pico (RP2040)";
                p.HardwarePreviewInterfaceType = "I2C";
                p.HardwarePreviewSdaPin = "GP4";
                p.HardwarePreviewSclPin = "GP5";
                p.HardwarePreviewI2cAddress = "0x3D";
                p.HardwarePreviewUseSoftwareI2c = true;
                p.HardwarePreviewDisplayModel = "SH1106 128x64";
                p.HardwarePreviewPort = "COM4";
                p.HardwarePreviewBaudRate = 921600;
            });

            var loaded = UserPreferencesService.Get();

            Assert.Equal("Raspberry Pi Pico (RP2040)", loaded.HardwarePreviewBoardPreset);
            Assert.Equal("I2C", loaded.HardwarePreviewInterfaceType);
            Assert.Equal("GP4", loaded.HardwarePreviewSdaPin);
            Assert.Equal("GP5", loaded.HardwarePreviewSclPin);
            Assert.Equal("0x3D", loaded.HardwarePreviewI2cAddress);
            Assert.True(loaded.HardwarePreviewUseSoftwareI2c);
            Assert.Equal("SH1106 128x64", loaded.HardwarePreviewDisplayModel);
            Assert.Equal("COM4", loaded.HardwarePreviewPort);
            Assert.Equal(921600, loaded.HardwarePreviewBaudRate);
        }

        [Fact]
        public void InterfaceTypeSwitch_I2cToSpi_UpdatesPinsToMatchBoard()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "I2C"
            };

            var vm = new HardwarePreviewWiringViewModel(config, 115200);
            Assert.Equal("I2C", vm.InterfaceType);
            Assert.Equal("21", vm.SdaPin);
            Assert.Equal("22", vm.SclPin);

            // Switch to SPI
            vm.InterfaceType = "SPI";

            Assert.Equal("SPI", vm.InterfaceType);
            Assert.Equal("5", vm.CsPin);
            Assert.Equal("16", vm.DcPin);
            Assert.Equal("17", vm.RstPin);
            Assert.Equal("18", vm.ClkPin);
            Assert.Equal("23", vm.MosiPin);
        }

        [Fact]
        public void InterfaceTypeSwitch_SpiToI2c_UpdatesPinsToMatchBoard()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                InterfaceType = "SPI"
            };

            var vm = new HardwarePreviewWiringViewModel(config, 115200);
            Assert.Equal("SPI", vm.InterfaceType);

            // Switch to I2C
            vm.InterfaceType = "I2C";

            Assert.Equal("I2C", vm.InterfaceType);
            Assert.Equal("A4", vm.SdaPin);
            Assert.Equal("A5", vm.SclPin);
        }

        [Fact]
        public void InterfaceTypeSwitch_CustomBoard_PreservesCustomPins()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Custom Board",
                InterfaceType = "I2C",
                SdaPin = "42",
                SclPin = "43",
                CsPin = "77",
                DcPin = "78"
            };

            var vm = new HardwarePreviewWiringViewModel(config, 115200);
            Assert.Equal("42", vm.SdaPin);
            Assert.Equal("43", vm.SclPin);

            // Switch to SPI on Custom Board
            vm.InterfaceType = "SPI";

            // Custom pins should NOT be overwritten by any board preset
            Assert.Equal("77", vm.CsPin);
            Assert.Equal("78", vm.DcPin);

            // Switch back to I2C
            vm.InterfaceType = "I2C";
            Assert.Equal("42", vm.SdaPin);
            Assert.Equal("43", vm.SclPin);
        }

        [Fact]
        public void UpdateStandaloneSketchInAppData_WritesToAppDataDirectory()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22"
            };

            bool success = HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(config, 921600);
            Assert.True(success);

            string expectedFile = Path.Combine(
                AssetsPathService.AppDataAssetsDirectory,
                AssetsPathService.HexpritePreviewStandaloneFolderName,
                AssetsPathService.StandaloneSketchFileName);

            Assert.True(File.Exists(expectedFile));
            string content = File.ReadAllText(expectedFile);
            Assert.Contains("#define SERIAL_BAUD 921600", content);
            Assert.Contains("#define I2C_SDA_PIN   21", content);
            Assert.Contains("#define I2C_SCL_PIN   22", content);
        }

        [Fact]
        public void NormalizePin_UppercasesAndTrims()
        {
            Assert.Equal("A4", HardwarePreviewWiringConfig.NormalizePin("  a4  "));
            Assert.Equal("PB7", HardwarePreviewWiringConfig.NormalizePin("pb7"));
            Assert.Equal("D1", HardwarePreviewWiringConfig.NormalizePin("d1"));
            Assert.Equal("GP4", HardwarePreviewWiringConfig.NormalizePin("gp4"));
            Assert.Equal("13", HardwarePreviewWiringConfig.NormalizePin("13"));
            Assert.Equal(string.Empty, HardwarePreviewWiringConfig.NormalizePin(null));
        }

        [Fact]
        public void NormalizeI2cAddress_Prepends0xAndUppercases()
        {
            Assert.Equal("0x3C", HardwarePreviewWiringConfig.NormalizeI2cAddress("3c"));
            Assert.Equal("0x3C", HardwarePreviewWiringConfig.NormalizeI2cAddress("0x3c"));
            Assert.Equal("0x3D", HardwarePreviewWiringConfig.NormalizeI2cAddress("3D"));
            Assert.Equal("0x3C", HardwarePreviewWiringConfig.NormalizeI2cAddress(null));
        }

        [Fact]
        public void Validation_NonHexAddress_ReturnsError()
        {
            var config = new HardwarePreviewWiringConfig
            {
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22",
                I2cAddress = "0x3G"
            };

            var result = config.Validate();
            Assert.False(result.IsValid);
            Assert.Contains("valid hex format", result.Error);
        }

        [Fact]
        public void Validation_SpiDuplicatePins_ReturnsError()
        {
            var config = new HardwarePreviewWiringConfig
            {
                InterfaceType = "SPI",
                CsPin = "10",
                DcPin = "10", // Duplicate
                ClkPin = "13",
                MosiPin = "11"
            };

            var result = config.Validate();
            Assert.False(result.IsValid);
            Assert.Contains("SPI pins must be unique", result.Error);
            Assert.Contains("CS and DC", result.Error);
        }

        [Fact]
        public void PlatformIOConfigGenerator_GeneratesExpectedDirectives()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "I2C",
                SdaPin = "21",
                SclPin = "22",
                I2cAddress = "0x3C",
                DisplayModel = "SSD1306 128x64"
            };

            string pioConfig = HardwarePreviewSketchGenerator.GeneratePlatformIOConfig(config, 921600);
            Assert.Contains("#define SERIAL_BAUD 921600", pioConfig);
            Assert.Contains("#define USE_I2C", pioConfig);
            Assert.Contains("#define I2C_SDA_PIN   21", pioConfig);
            Assert.Contains("#define I2C_SCL_PIN   22", pioConfig);
            Assert.Contains("#define I2C_ADDRESS   0x3C", pioConfig);
            Assert.Contains("#define DISPLAY_SSD1306_128X64_I2C", pioConfig);
        }

        [Fact]
        public void CanvasBufferWarning_ExceedsUnoRAM_ShowsWarning()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Arduino Uno / Nano");

            // 256x256 sprite requires ~8,200 bytes, which exceeds Uno's 1,040 byte buffer
            var vm = new HardwarePreviewWiringViewModel(config, baudRate: 115200, canvasWidth: 256, canvasHeight: 256);

            Assert.True(vm.HasCanvasBufferWarning);
            Assert.NotNull(vm.CanvasBufferWarning);
            Assert.Contains("exceeds Arduino Uno / Nano's RAM buffer", vm.CanvasBufferWarning);
        }

        [Fact]
        public void CanvasBufferWarning_WithinUnoRAM_NoWarning()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("Arduino Uno / Nano");

            // 128x64 sprite requires ~1,031 bytes, which fits Uno's 1,040 byte buffer
            var vm = new HardwarePreviewWiringViewModel(config, baudRate: 115200, canvasWidth: 128, canvasHeight: 64);

            Assert.False(vm.HasCanvasBufferWarning);
            Assert.Null(vm.CanvasBufferWarning);
        }

        [Fact]
        public void PinEdit_AutoSwitchesPresetToCustomBoard()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("ESP32 DevKit");

            var vm = new HardwarePreviewWiringViewModel(config);
            Assert.Equal("ESP32 DevKit", vm.SelectedBoardPreset);

            // Change SDA from 21 to 4
            vm.SdaPin = "4";

            Assert.Equal("Custom Board", vm.SelectedBoardPreset);
        }

        [Fact]
        public void PlatformIOIniGenerator_ESP32_SetsEsp32DefaultEnv()
        {
            string ini = HardwarePreviewSketchGenerator.GeneratePlatformIOIni("ESP32 DevKit", 115200);

            Assert.Contains("default_envs = esp32", ini);
            Assert.Contains("[env:esp32]", ini);
            Assert.Contains("monitor_speed = 115200", ini);
            Assert.Contains("olikraus/U8g2", ini);
        }

        [Fact]
        public void PlatformIOIniGenerator_Uno_SetsUnoDefaultEnv()
        {
            string ini = HardwarePreviewSketchGenerator.GeneratePlatformIOIni("Arduino Uno / Nano", 115200);

            Assert.Contains("default_envs = uno", ini);
            Assert.Contains("[env:uno]", ini);
            Assert.Contains("board = uno", ini);
        }

        [Fact]
        public void HardwarePreviewWiringViewModel_PlatformIOProperties_ArePopulated()
        {
            var config = new HardwarePreviewWiringConfig();
            config.ApplyPreset("ESP32 DevKit");

            var vm = new HardwarePreviewWiringViewModel(config, baudRate: 115200);

            Assert.NotNull(vm.GeneratedPlatformIOConfig);
            Assert.Contains("I2C_SDA_PIN", vm.GeneratedPlatformIOConfig);
            Assert.NotNull(vm.GeneratedPlatformIOIni);
            Assert.Contains("default_envs = esp32", vm.GeneratedPlatformIOIni);
        }

        [Theory]
        [InlineData("SSD1306 128x64", 128, 64)]
        [InlineData("SSD1306 128x32", 128, 32)]
        [InlineData("SH1106 128x64", 128, 64)]
        [InlineData("SSD1327 128x128", 128, 128)]
        [InlineData(null, 128, 64)]
        public void GetDisplayDimensions_ReturnsExpectedSize(string? model, int expectedW, int expectedH)
        {
            var (w, h) = HardwarePreviewWiringConfig.GetDisplayDimensions(model);
            Assert.Equal(expectedW, w);
            Assert.Equal(expectedH, h);
        }

        [Fact]
        public void GenerateArduinoSketch_ContainsBufferIndexBoundsCheck()
        {
            var config = new HardwarePreviewWiringConfig();
            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);

            Assert.Contains("if (bufferIndex >= MAX_BUFFER)", sketch);
        }

        [Fact]
        public void GenerateArduinoSketch_DoesNotContainBrokenAvrPreprocCheck()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                InterfaceType = "I2C",
                SdaPin = "18",
                SclPin = "19"
            };
            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);

            Assert.DoesNotContain("I2C_SDA_PIN != SDA", sketch);
        }

        [Fact]
        public void HardwarePreviewService_ResetConnectionForBaudProbe_SetsConnectingState()
        {
            using var service = new HardwarePreviewService();
            service.ResetConnectionForBaudProbe();

            Assert.Equal(HardwarePreviewConnectionState.Connecting, service.ConnectionState);
        }

        [Fact]
        public void LibrarySnippet_IncludesSerialBeginWithBaudRate()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                InterfaceType = "I2C",
                SdaPin = "A4",
                SclPin = "A5"
            };

            string snippetDefault = HardwarePreviewSketchGenerator.GenerateLibrarySnippet(config);
            Assert.Contains("Serial.begin(115200);", snippetDefault);

            string snippetCustom = HardwarePreviewSketchGenerator.GenerateLibrarySnippet(config, 57600);
            Assert.Contains("Serial.begin(57600);", snippetCustom);
        }

        [Fact]
        public void SketchGenerator_SPI_EmptyRstPin_EmitsU8X8PinNone()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "ESP32 DevKit",
                InterfaceType = "SPI",
                CsPin = "5",
                DcPin = "16",
                RstPin = "",
                ClkPin = "18",
                MosiPin = "23",
                DisplayModel = "SSD1306 128x64"
            };

            string sketch = HardwarePreviewSketchGenerator.GenerateArduinoSketch(config, 115200);
            Assert.Contains("#define SPI_RST_PIN   U8X8_PIN_NONE", sketch);
        }

        [Fact]
        public void Validation_ST7920WithI2C_ReturnsError()
        {
            var config = new HardwarePreviewWiringConfig
            {
                DisplayModel = "ST7920 128x64",
                InterfaceType = "I2C",
                SdaPin = "A4",
                SclPin = "A5"
            };

            var result = config.Validate();
            Assert.False(result.IsValid);
            Assert.NotNull(result.Error);
            Assert.Contains("ST7920 128x64 does not support I2C", result.Error);
        }

        [Fact]
        public void StandaloneSketchTemplate_ContainsSoftwareI2cConstructors()
        {
            string templatePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Hexprite", "Assets", "HexpritePreview-Standalone-Arduino", "HexpritePreview-Standalone-Arduino.ino"));
            Assert.True(File.Exists(templatePath), $"File should exist at {templatePath}");
            string content = File.ReadAllText(templatePath);
            Assert.Contains("#if defined(USE_SOFTWARE_I2C)", content);
            Assert.Contains("U8G2_SSD1306_128X64_NONAME_F_SW_I2C", content);
        }

        [Fact]
        public void FirmwareFiles_UseUint32ForPacketSize()
        {
            string baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Hexprite", "Assets"));
            string cppPath = Path.Combine(baseDir, "HexpritePreview", "src", "HexpritePreview.cpp");
            string inoPath = Path.Combine(baseDir, "HexpritePreview-Standalone-Arduino", "HexpritePreview-Standalone-Arduino.ino");
            string pioPath = Path.Combine(baseDir, "HexpritePreview-Standalone-PlatformIO", "src", "main.cpp");

            Assert.True(File.Exists(cppPath), $"File should exist at {cppPath}");
            Assert.True(File.Exists(inoPath), $"File should exist at {inoPath}");
            Assert.True(File.Exists(pioPath), $"File should exist at {pioPath}");

            string cpp = File.ReadAllText(cppPath);
            string ino = File.ReadAllText(inoPath);
            string pio = File.ReadAllText(pioPath);

            Assert.Contains("uint32_t dataSize", cpp);
            Assert.Contains("uint32_t totalSize", cpp);

            Assert.Contains("uint32_t dataSize", ino);
            Assert.Contains("uint32_t totalSize", ino);

            Assert.Contains("uint32_t dataSize", pio);
            Assert.Contains("uint32_t totalSize", pio);
        }

        [Fact]
        public void CanvasBufferWarning_ConsidersTargetDisplayDimensions()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                DisplayModel = "SSD1327 128x128", // 128x128 = 2048 bytes > 1040 buffer
                InterfaceType = "SPI",
                CsPin = "10",
                DcPin = "9",
                ClkPin = "13",
                MosiPin = "11"
            };

            // Canvas is small (16x16), but target display is 128x128 which exceeds Uno's 1040B buffer
            var vm = new HardwarePreviewWiringViewModel(config, 115200, canvasWidth: 16, canvasHeight: 16);

            Assert.True(vm.HasCanvasBufferWarning);
            Assert.NotNull(vm.CanvasBufferWarning);
            Assert.Contains("exceeds", vm.CanvasBufferWarning);
        }

        [Fact]
        public async System.Threading.Tasks.Task AutoDetectBaudRate_ProbesWithRawCanvasDimensions_NotDoubleScaled()
        {
            var config = new HardwarePreviewWiringConfig
            {
                BoardPreset = "Arduino Uno / Nano",
                DisplayModel = "SSD1306 128x64"
            };

            var hwMock = new Moq.Mock<IHardwarePreviewService>();
            hwMock.SetupProperty(h => h.IsEnabled, true);
            hwMock.SetupProperty(h => h.BaudRate, 115200);
            // Simulate that LastTransmittedFrame has transformed dimensions (128x64)
            hwMock.SetupGet(h => h.LastTransmittedFrame).Returns((new bool[128 * 64], 128, 64));

            // Canvas is 16x16
            var vm = new HardwarePreviewWiringViewModel(config, 115200, canvasWidth: 16, canvasHeight: 16, hardwarePreview: hwMock.Object);

            await vm.AutoDetectBaudRateCommand.ExecuteAsync(null);

            // Must probe with raw canvas dimensions (16, 16), NOT the already-transformed (128, 64)
            hwMock.Verify(h => h.SendFrame(Moq.It.IsAny<bool[]>(), 16, 16), Moq.Times.AtLeastOnce());
            hwMock.Verify(h => h.SendFrame(Moq.It.IsAny<bool[]>(), 128, 64), Moq.Times.Never());
        }
    }
}
