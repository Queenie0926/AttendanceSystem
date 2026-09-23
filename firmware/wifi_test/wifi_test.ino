/*
 * Wi-Fi only test - diagnostic, not part of the attendance device.
 *
 * The absolute minimum: serial and the radio. No SPI, no I2C, no keypad,
 * no GPIO configured at all.
 *
 * PHYSICALLY DISCONNECT EVERYTHING before running this. Take the ESP32 out
 * of the expansion board. No OLED, no RC522, no DS3231, no keypad, no LEDs,
 * no buzzer, no buttons, no DFPlayer. Just the devkit and a USB cable.
 *
 * Reads:
 *   Connects and prints an IP  -> the board and supply are fine, and
 *                                 something on the 3.3V rail was the load.
 *                                 Add peripherals back one at a time.
 *   Brownout anyway            -> the devkit's regulator, the cable or the
 *                                 supply. No wiring change will fix it;
 *                                 swap the board.
 */

#include <WiFi.h>
#include "arduino_secrets.h"

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println(F("=== Wi-Fi only test ==="));
  Serial.print(F("Chip: "));
  Serial.println(ESP.getChipModel());

  Serial.println(F("Starting radio at full power..."));
  WiFi.mode(WIFI_STA);
  WiFi.begin(SECRET_WIFI_SSID, SECRET_WIFI_PASSWORD);

  // If the board survives to here, the largest spike is already past.
  Serial.println(F("Radio up - no brownout during init."));

  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < 20000) {
    delay(500);
    Serial.print('.');
  }
  Serial.println();

  if (WiFi.status() == WL_CONNECTED) {
    Serial.print(F("CONNECTED. IP: "));
    Serial.println(WiFi.localIP());
    Serial.print(F("Signal: "));
    Serial.print(WiFi.RSSI());
    Serial.println(F(" dBm"));
  } else {
    // Not a power problem - the radio survived, the credentials or the
    // access point are the issue.
    Serial.println(F("Radio survived but did not connect - check SSID/password."));
  }
}

void loop() {
  // Transmitting is what draws current. Keep poking the network so a
  // marginal supply has every chance to show itself.
  Serial.print(F("up "));
  Serial.print(millis() / 1000);
  Serial.print(F("s, status "));
  Serial.print(WiFi.status() == WL_CONNECTED ? F("connected, RSSI ") : F("disconnected ("));
  Serial.println(WiFi.status() == WL_CONNECTED ? String(WiFi.RSSI()) : String(WiFi.status()) + ")");
  delay(2000);
}
