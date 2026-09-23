/*
 * I2C scanner - diagnostic only, not part of the attendance device.
 *
 * Upload this, open Serial Monitor at 115200, and it lists every address
 * that answers on the OLED/RTC bus (SDA 21, SCL 22).
 *
 * Expected on a healthy board:
 *   0x3C  SSD1306 OLED   (some modules are 0x3D instead)
 *   0x68  DS3231 RTC
 *   0x57  AT24C32 EEPROM - present on most DS3231 modules, harmless
 *
 * Nothing found at all = wiring or power, not addressing. Check that SDA
 * and SCL are not swapped and that the module has 3.3V and GND.
 */

#include <Wire.h>

void setup() {
  Serial.begin(115200);
  delay(1000);
  Wire.begin(21, 22); // same pins as the attendance firmware
  Serial.println(F("\nI2C scanner ready."));
}

void loop() {
  int found = 0;

  Serial.println(F("Scanning..."));
  for (uint8_t address = 1; address < 127; address++) {
    Wire.beginTransmission(address);
    uint8_t error = Wire.endTransmission();

    if (error == 0) {
      Serial.print(F("  device at 0x"));
      if (address < 16) Serial.print('0');
      Serial.print(address, HEX);

      if (address == 0x3C || address == 0x3D) Serial.print(F("  <- SSD1306 OLED"));
      if (address == 0x68) Serial.print(F("  <- DS3231 RTC"));
      if (address == 0x57) Serial.print(F("  <- DS3231 EEPROM"));
      Serial.println();
      found++;
    }
  }

  if (found == 0) {
    Serial.println(F("  nothing found - check SDA/SCL wiring and 3.3V power"));
  } else {
    Serial.print(F("  "));
    Serial.print(found);
    Serial.println(F(" device(s)"));
  }

  Serial.println();
  delay(3000);
}
