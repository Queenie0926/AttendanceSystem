// RFID UID reader test for the RC522 on ESP32.
// Tap a card and its UID prints to Serial Monitor (115200 baud).
//
// Wiring (same as attendance_device):
//   RC522 SDA  -> GPIO 5
//   RC522 SCK  -> GPIO 18
//   RC522 MOSI -> GPIO 23
//   RC522 MISO -> GPIO 19
//   RC522 RST  -> GPIO 17
//   RC522 3.3V -> 3V3   (NOT 5V)
//   RC522 GND  -> GND

//   OLED SDA   -> GPIO 21
//   OLED SCL   -> GPIO 22

#include <SPI.h>
#include <Wire.h>
#include <MFRC522.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

#define SCREEN_WIDTH       128
#define SCREEN_HEIGHT      64
#define SCREEN_ADDRESS     0x3C
#define SCREEN_ADDRESS_ALT 0x3D
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, -1);
bool displayReady = false;

void showOled(const String& line1, const String& line2, const String& line3 = "") {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1);
  display.setCursor(0, 0);
  display.println(line1);
  display.setTextSize(2);
  display.setCursor(0, 20);
  display.println(line2);
  display.setTextSize(1);
  display.setCursor(0, 54);
  display.println(line3);
  display.display();
}

#define RC522_SS_PIN   5
#define RC522_RST_PIN  17
#define SPI_SCK_PIN    18
#define SPI_MISO_PIN   19
#define SPI_MOSI_PIN   23

MFRC522 rfid(RC522_SS_PIN, RC522_RST_PIN);

void setup() {
  Serial.begin(115200);
  delay(500);

  Wire.begin(21, 22);
  displayReady = display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)
              || display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS_ALT);
  if (!displayReady) Serial.println("OLED not found - Serial output only.");

  SPI.begin(SPI_SCK_PIN, SPI_MISO_PIN, SPI_MOSI_PIN, RC522_SS_PIN);
  rfid.PCD_Init();
  delay(50);

  byte version = rfid.PCD_ReadRegister(MFRC522::VersionReg);
  Serial.printf("RC522 version: 0x%02X\n", version);
  if (version == 0x00 || version == 0xFF) {
    Serial.println("RC522 NOT detected - check wiring and 3.3V power.");
    showOled("RC522 ERROR", "No RFID", "Check wiring/3.3V");
  } else {
    Serial.println("RC522 ready. Tap a card...");
    showOled("UID Reader", "Tap card", "Waiting...");
  }
}

void loop() {
  if (!rfid.PICC_IsNewCardPresent() || !rfid.PICC_ReadCardSerial()) {
    return;
  }

  String uid = "";
  for (byte i = 0; i < rfid.uid.size; i++) {
    if (rfid.uid.uidByte[i] < 0x10) uid += "0";
    uid += String(rfid.uid.uidByte[i], HEX);
  }
  uid.toUpperCase();

  Serial.println("----------------------------");
  Serial.print("UID (hex):    ");
  Serial.println(uid);
  Serial.print("UID (spaced): ");
  for (byte i = 0; i < rfid.uid.size; i++) {
    Serial.printf("%02X ", rfid.uid.uidByte[i]);
  }
  Serial.println();
  Serial.print("Card type:    ");
  Serial.println(rfid.PICC_GetTypeName(rfid.PICC_GetType(rfid.uid.sak)));

  // Size-2 text fits 10 chars per line; a 7-byte UID (14 chars) wraps.
  showOled("Card UID:", uid, "Tap another card");

  rfid.PICC_HaltA();
  rfid.PCD_StopCrypto1();
  delay(1000);
}
