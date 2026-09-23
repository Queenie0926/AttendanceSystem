/*
 * Hardware test sketch - RC522 version (no Wi-Fi, no DFPlayer).
 *
 * Offline check of the OLED, RTC, keypad, RFID reader, LEDs, buzzer and the
 * two attendance buttons. PINs are hardcoded below; nothing is sent anywhere.
 *
 * Wiring:
 *   RC522  SCK/MISO/MOSI -> GPIO18 / 19 / 23   SDA(SS) -> GPIO5   RST -> GPIO17
 *   OLED + DS3231 (I2C)  -> SDA GPIO21, SCL GPIO22, 3.3V, GND
 *   Keypad rows          -> GPIO13, 12, 14, 27
 *   Keypad cols          -> GPIO15, 33, 25, 32
 *   Green LED GPIO2, Red LED GPIO16, Buzzer GPIO26
 *   Time-In GPIO34, Time-Out GPIO35 (input-only: external 10k pull-down)
 */

#include <SPI.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <RTClib.h>
#include <Keypad.h>
#include <MFRC522.h>

// --- OLED Display Configuration (I2C) ---
#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_RESET    -1
#define SCREEN_ADDRESS 0x3C
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

// --- DS3231 RTC Configuration (I2C) ---
RTC_DS3231 rtc;

// --- RC522 Pin Definitions (SPI) ---
#define RC522_SS_PIN   5   // D5  (SDA on the module silkscreen)
#define RC522_RST_PIN  17  // TX2 / GPIO 17
#define SPI_SCK_PIN    18
#define SPI_MISO_PIN   19
#define SPI_MOSI_PIN   23

MFRC522 rfid(RC522_SS_PIN, RC522_RST_PIN);

// --- 4x4 Matrix Keypad Configuration ---
const byte ROWS = 4;
const byte COLS = 4;
char keys[ROWS][COLS] = {
  {'1','2','3','A'},
  {'4','5','6','B'},
  {'7','8','9','C'},
  {'*','0','#','D'}
};
byte rowPins[ROWS] = {13, 12, 14, 27}; // D13, D12, D14, D27
byte colPins[COLS] = {15, 33, 25, 32}; // D15, D33, D25, D32

Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);

// --- Feedback Pins ---
const int GREEN_LED  = 2;   // D2 (Success LED)
const int RED_LED    = 16;  // RX2 / GPIO 16 (Error LED)
const int BUZZER_PIN = 26;  // D26 (Buzzer)

// --- Physical Attendance Buttons ---
const int TIME_IN_BTN = 34;  // Pin 34 (Green Button)
const int TIME_OUT_BTN = 35; // Pin 35 (Red Button)

// --- Authorized PINs Configuration ---
const char* validPins[] = {"5681", "7852", "3698"};
const int totalValidPins = 3;

// System States & Variables
String detectedUID = "";
bool waitingForPinAndMode = false;
String enteredPin = "";

void updateDisplay(String message1, String message2) {
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 0);
  display.println(F("ATTENDANCE SYSTEM"));
  display.println(F("-----------------"));
  display.println(message1);
  display.println(message2);
  display.display();
}

bool checkPinAuthorization(String pin) {
  for (int i = 0; i < totalValidPins; i++) {
    if (pin == validPins[i]) {
      return true;
    }
  }
  return false;
}

void setup() {
  Serial.begin(115200);
  delay(1000);

  // Initialize shared I2C bus for OLED and RTC (SDA = D21, SCL = D22)
  Wire.begin(21, 22);

  // Initialize OLED Display
  if(!display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)) {
    Serial.println(F("SSD1306 allocation failed!"));
    for(;;);
  }

  display.display();
  delay(1000);
  updateDisplay("System Booting...", "Please Wait...");

  // Initialize RTC Module
  if (!rtc.begin()) {
    Serial.println(F("Couldn't find RTC!"));
  }
  if (rtc.lostPower()) {
    Serial.println(F("RTC lost power, setting time to compile time!"));
    rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));
  }

  // Configure feedback pins
  pinMode(GREEN_LED, OUTPUT);
  pinMode(RED_LED, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);

  digitalWrite(GREEN_LED, LOW);
  digitalWrite(RED_LED, LOW);
  digitalWrite(BUZZER_PIN, LOW);

  // Configure Attendance Buttons
  pinMode(TIME_IN_BTN, INPUT);
  pinMode(TIME_OUT_BTN, INPUT);

  // Initialize RC522 reader
  SPI.begin(SPI_SCK_PIN, SPI_MISO_PIN, SPI_MOSI_PIN, RC522_SS_PIN);
  rfid.PCD_Init();

  // The library has no "is it connected" call, so read the version register.
  // 0x00 or 0xFF means nothing is answering on SPI.
  byte version = rfid.PCD_ReadRegister(MFRC522::VersionReg);
  if (version == 0x00 || version == 0xFF) {
    Serial.println(F("RC522 not responding - check SPI wiring and 3.3V."));
  } else {
    Serial.print(F("RC522 ready, version 0x"));
    Serial.println(version, HEX);
  }

  updateDisplay("Ready!", "Tap your RFID card");
  Serial.println(F("System Ready."));
}

void loop() {
  if (!waitingForPinAndMode) {
    // RC522 reports a card in two steps: is one present, then read its serial.
    if (rfid.PICC_IsNewCardPresent() && rfid.PICC_ReadCardSerial()) {
      uint8_t uidLen = rfid.uid.size;

      if (uidLen == 4 || uidLen == 7 || uidLen == 10) {
        detectedUID = "";
        for (uint8_t i = 0; i < uidLen; i++) {
          if (rfid.uid.uidByte[i] < 0x10) detectedUID += "0";
          detectedUID += String(rfid.uid.uidByte[i], HEX);
        }
        detectedUID.toUpperCase();

        Serial.print(F("Card UID: "));
        Serial.println(detectedUID);

        beep(100);
        waitingForPinAndMode = true;
        enteredPin = "";

        updateDisplay("Card Detected!", "Enter PIN & Button");
        digitalWrite(GREEN_LED, HIGH);
        delay(200);
        digitalWrite(GREEN_LED, LOW);
      }

      // Stop talking to this card so the next tap is seen as a new one.
      rfid.PICC_HaltA();
      rfid.PCD_StopCrypto1();
    }
  }
  else {
    char key = keypad.getKey();
    if (key) {
      beep(50);
      if (key == '*') {
        enteredPin = "";
        updateDisplay("PIN Cleared", "Re-enter PIN:");
      }
      else if (key != '#') {
        enteredPin += key;
        String masked = "";
        for (unsigned int i = 0; i < enteredPin.length(); i++) masked += "*";
        updateDisplay("PIN:", masked);
      }
    }

    if (digitalRead(TIME_IN_BTN) == HIGH) {
      delay(50);
      if (digitalRead(TIME_IN_BTN) == HIGH) {
        processAttendance("TIME-IN");
        while(digitalRead(TIME_IN_BTN) == HIGH) delay(10);
      }
    }

    if (digitalRead(TIME_OUT_BTN) == HIGH) {
      delay(50);
      if (digitalRead(TIME_OUT_BTN) == HIGH) {
        processAttendance("TIME-OUT");
        while(digitalRead(TIME_OUT_BTN) == HIGH) delay(10);
      }
    }
  }

  delay(50);
}

void processAttendance(String attendanceType) {
  if (checkPinAuthorization(enteredPin)) {
    // Fetch real-time timestamp from RTC module
    DateTime now = rtc.now();
    char timeBuffer[20];
    sprintf(timeBuffer, "%02d/%02d %02d:%02d:%02d", now.month(), now.day(), now.hour(), now.minute(), now.second());

    updateDisplay("SUCCESS!", attendanceType);
    Serial.print(F("Logged UID: "));
    Serial.print(detectedUID);
    Serial.print(F(" | "));
    Serial.print(attendanceType);
    Serial.print(F(" at "));
    Serial.println(timeBuffer);

    digitalWrite(GREEN_LED, HIGH);
    beep(200); delay(100); beep(200);
    digitalWrite(GREEN_LED, LOW);
  } else {
    updateDisplay("ACCESS DENIED", "Incorrect PIN!");
    digitalWrite(RED_LED, HIGH);
    beep(600);
    digitalWrite(RED_LED, LOW);
  }

  waitingForPinAndMode = false;
  enteredPin = "";
  delay(2000);
  updateDisplay("Ready!", "Tap your RFID card");
}

void beep(int duration) {
  digitalWrite(BUZZER_PIN, HIGH);
  delay(duration);
  digitalWrite(BUZZER_PIN, LOW);
}
