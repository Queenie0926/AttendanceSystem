/*
 * Attendance System - ESP32 device firmware (no audio build)
 *
 * Wiring (matches the approved component list):
 *   RC522  SCK/MISO/MOSI -> GPIO18 / 19 / 23     SDA(SS) -> GPIO5   RST -> GPIO17
 *   OLED + DS3231 (I2C)  -> SDA GPIO21, SCL GPIO22, 3.3V, GND
 *   Keypad rows          -> GPIO13, 12, 14, 27
 *   Keypad cols          -> GPIO15, 33, 25, 32
 *   Green LED GPIO2, Red LED GPIO16, Buzzer GPIO26
 *   Time-In GPIO34, Time-Out GPIO35 (input-only: external 10k pull-down required)
 *
 * No DFPlayer Mini in this build: feedback is the OLED, the LEDs and the
 * buzzer only. GPIO4 and GPIO36 are unused and free.
 *
 * Wi-Fi is required: the OTP send/verify calls go to Supabase Edge Functions.
 */

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <SPI.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include <RTClib.h>
#include <Keypad.h>
#include <MFRC522.h>
#include <time.h>

// ---------- CONFIG ----------
// Real values live in arduino_secrets.h, which is gitignored.
// Copy arduino_secrets.example.h to arduino_secrets.h and fill it in.
#include "arduino_secrets.h"

const char* WIFI_SSID     = SECRET_WIFI_SSID;
const char* WIFI_PASSWORD = SECRET_WIFI_PASSWORD;
const char* SEND_OTP_URL   = "https://" SECRET_SUPABASE_REF ".functions.supabase.co/send-otp";
const char* VERIFY_OTP_URL = "https://" SECRET_SUPABASE_REF ".functions.supabase.co/verify-otp";
const char* DEVICE_API_KEY = SECRET_DEVICE_KEY;

// --- RC522 RFID (SPI) ---
#define RC522_SS_PIN   5
#define RC522_RST_PIN  17
MFRC522 rfid(RC522_SS_PIN, RC522_RST_PIN);

// --- OLED (I2C) ---
#define SCREEN_WIDTH   128
#define SCREEN_HEIGHT  64
#define OLED_RESET     -1
#define SCREEN_ADDRESS     0x3C
#define SCREEN_ADDRESS_ALT 0x3D   // some SSD1306 modules ship on this address
bool displayReady = false;
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

// --- DS3231 RTC (I2C, shares bus with OLED) ---
RTC_DS3231 rtc;

// --- 4x4 Keypad ---
const byte ROWS = 4, COLS = 4;
char keys[ROWS][COLS] = {
  {'1','2','3','A'},
  {'4','5','6','B'},
  {'7','8','9','C'},
  {'*','0','#','D'}
};
byte rowPins[ROWS] = {13, 12, 14, 27};
byte colPins[COLS] = {15, 33, 25, 32};
Keypad keypad = Keypad(makeKeymap(keys), rowPins, colPins, ROWS, COLS);

// --- Feedback pins ---
#define GREEN_LED_PIN 2
#define RED_LED_PIN   16
#define BUZZER_PIN    26

// --- Attendance buttons (active-HIGH; input-only pins need external 10k pull-down) ---
#define BTN_TIME_IN   34
#define BTN_TIME_OUT  35

// ---------- TYPES (declared before first use) ----------
struct ScanResult   { String status; String staffName; int waitSeconds; };
struct VerifyResult { String status; String staffName; String eventType; bool isLate; String expected; };

// ---------- STATE ----------
// STATE_ prefix: the Keypad library declares a global enum with IDLE in it
// (KeyState), so unprefixed names collide at compile time.
enum SystemState { STATE_IDLE, STATE_AWAITING_OTP, STATE_AWAITING_ACTION };
SystemState state = STATE_IDLE;

String currentUid = "";
String otpBuffer = "";
unsigned long otpStartTime = 0;
const unsigned long OTP_TIMEOUT_MS = 90000; // matches send-otp's OTP_EXPIRY_SECONDS

// ---------- FORWARD DECLARATIONS ----------
ScanResult   requestOtp(String uid);
VerifyResult verifyOtp(String uid, String otp, String action);
void showMessage(String line1, String line2);
void showMessage3(String line1, String line2, String line3);
void beep(int times, int durationMs);
void waitForButtonRelease();

// ---------- SETUP ----------
void setup() {
  Serial.begin(115200);
  delay(1000);

  pinMode(GREEN_LED_PIN, OUTPUT);
  pinMode(RED_LED_PIN, OUTPUT);
  pinMode(BUZZER_PIN, OUTPUT);
  digitalWrite(GREEN_LED_PIN, LOW);
  digitalWrite(RED_LED_PIN, LOW);
  digitalWrite(BUZZER_PIN, LOW);

  pinMode(BTN_TIME_IN, INPUT);   // external pull-down required, see wiring note
  pinMode(BTN_TIME_OUT, INPUT);  // external pull-down required, see wiring note

  Wire.begin(21, 22); // SDA, SCL - shared by OLED and DS3231

  // Modules ship at either 0x3C or 0x3D - try both before giving up.
  displayReady = display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)
              || display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS_ALT);

  if (displayReady) {
    // Draw something immediately: if setup() dies later (a brownout during
    // audio or Wi-Fi init), a blank screen and a working screen look
    // identical. This proves the panel is alive before that can happen.
    showMessage("Starting up...", "");
  } else {
    // Do NOT hang here: the reader, keypad and audio work fine without a
    // screen, and a dead OLED should not take the whole device down.
    // showMessage() mirrors to serial, so the flow stays debuggable.
    Serial.println(F("SSD1306 not found at 0x3C or 0x3D - running without a display."));
    Serial.println(F("Check SDA=21, SCL=22, 3.3V, GND. Run firmware/i2c_scanner to list the bus."));
  }

  if (!rtc.begin()) {
    Serial.println(F("Couldn't find DS3231 - check wiring."));
  } else if (rtc.lostPower()) {
    Serial.println(F("RTC lost power, using compile time until NTP sync."));
    rtc.adjust(DateTime(F(__DATE__), F(__TIME__)));
  }

  SPI.begin(18, 19, 23, RC522_SS_PIN); // SCK, MISO, MOSI, SS
  rfid.PCD_Init();

  // The MFRC522 library has no "is it there" call, so read the version
  // register directly. 0x00 or 0xFF means nothing is answering on SPI.
  byte rc522Version = rfid.PCD_ReadRegister(MFRC522::VersionReg);
  if (rc522Version == 0x00 || rc522Version == 0xFF) {
    Serial.println(F("RC522 not responding - check SPI wiring and 3.3V."));
  } else {
    Serial.print(F("RC522 ready, version 0x"));
    Serial.println(rc522Version, HEX);
  }

  connectWiFi();
  syncRtcFromNtp();
  showMessage("Ready.", "Tap your ID.");
}

void connectWiFi() {
  showMessage("Connecting", "to Wi-Fi...");

  WiFi.mode(WIFI_STA);

  // Turning the radio on is the largest current spike in the whole boot
  // (~350-500mA at full power). Capping transmit power cuts that spike and
  // is often enough to stop a marginal supply triggering the brownout
  // detector. This is a MITIGATION, not a fix - a supply that cannot hold
  // 3.3V here is still a supply that needs sorting out. Range is roughly
  // 20dBm (default, max) down to 2dBm; raise it if the device ends up far
  // from the access point and the connection is unreliable.
  WiFi.setTxPower(WIFI_POWER_11dBm);

  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < 30000) {
    delay(400);
    Serial.print(".");
  }
  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\nWi-Fi connected: " + WiFi.localIP().toString());
  } else {
    Serial.println(F("\nWi-Fi connect timed out - will retry in the background."));
  }
}

void ensureWiFi() {
  if (WiFi.status() == WL_CONNECTED) return;
  WiFi.disconnect();
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < 8000) delay(200);
}

void syncRtcFromNtp() {
  if (WiFi.status() != WL_CONNECTED) return;
  configTime(8 * 3600, 0, "pool.ntp.org", "time.nist.gov"); // GMT+8, Asia/Manila
  struct tm timeinfo;
  if (getLocalTime(&timeinfo, 5000)) {
    rtc.adjust(DateTime(timeinfo.tm_year + 1900, timeinfo.tm_mon + 1, timeinfo.tm_mday,
                        timeinfo.tm_hour, timeinfo.tm_min, timeinfo.tm_sec));
    Serial.println(F("RTC synced from NTP."));
  } else {
    Serial.println(F("NTP sync failed - keeping existing RTC time."));
  }
}

// ---------- MAIN LOOP ----------
void loop() {
  switch (state) {
    case STATE_IDLE:            handleIdle(); break;
    case STATE_AWAITING_OTP:    handleAwaitingOtp(); break;
    case STATE_AWAITING_ACTION: handleAwaitingAction(); break;
  }
}

void handleIdle() {
  if (!rfid.PICC_IsNewCardPresent()) return;
  if (!rfid.PICC_ReadCardSerial()) return;

  uint8_t uidLength = rfid.uid.size;
  String uid = bytesToHexString(rfid.uid.uidByte, uidLength);
  rfid.PICC_HaltA();
  rfid.PCD_StopCrypto1();

  // Only accept standard UID lengths - filters out garbage/partial reads
  if (uidLength != 4 && uidLength != 7 && uidLength != 10) return;

  // Short confirm beep the instant the card reads.
  beep(1, 80);
  showMessage("Reading card...", "");
  ScanResult result = requestOtp(uid);

  if (result.status == "sent") {
    currentUid = uid;
    otpBuffer = "";
    otpStartTime = millis();
    state = STATE_AWAITING_OTP;
    showMessage("Hi " + result.staffName + "!", "Enter emailed code:");
  } else if (result.status == "cooldown") {
    beep(1, 150);
    showMessage("Please wait", String(result.waitSeconds) + "s and retap");
    delay(2000);
    showMessage("Ready.", "Tap your ID.");
  } else if (result.status == "unregistered") {
    beep(3, 100);
    showMessage("Card not", "registered.");
    delay(2000);
    showMessage("Ready.", "Tap your ID.");
  } else {
    beep(2, 150);
    showMessage("Connection", "error. Retry.");
    delay(2000);
    showMessage("Ready.", "Tap your ID.");
  }
}

void handleAwaitingOtp() {
  if (millis() - otpStartTime > OTP_TIMEOUT_MS) {
    beep(2, 150);
    showMessage("Code expired.", "Please tap again.");
    delay(2000);
    resetToIdle();
    return;
  }

  char key = keypad.getKey();
  if (!key) return;

  if (key == '*') {
    showMessage("Cancelled.", "Tap your ID.");
    delay(1500);
    resetToIdle();
    return;
  }

  if (isDigit(key) && otpBuffer.length() < 6) {
    otpBuffer += key;
    String masked = "";
    for (unsigned i = 0; i < otpBuffer.length(); i++) masked += "*";
    showMessage("Code: " + masked, "# to confirm");
  }

  bool shouldConfirm = (key == '#' && otpBuffer.length() > 0) || otpBuffer.length() == 6;
  if (shouldConfirm) {
    state = STATE_AWAITING_ACTION;
    showMessage("Press TIME IN", "or TIME OUT");
  }
}

void handleAwaitingAction() {
  if (millis() - otpStartTime > OTP_TIMEOUT_MS) {
    beep(2, 150);
    showMessage("Code expired.", "Please tap again.");
    delay(2000);
    resetToIdle();
    return;
  }

  String action = "";
  if (digitalRead(BTN_TIME_IN) == HIGH) {
    delay(50); // debounce
    if (digitalRead(BTN_TIME_IN) == HIGH) action = "TIME-IN";
  } else if (digitalRead(BTN_TIME_OUT) == HIGH) {
    delay(50);
    if (digitalRead(BTN_TIME_OUT) == HIGH) action = "TIME-OUT";
  }
  if (action == "") return;

  showMessage("Verifying...", "");
  VerifyResult result = verifyOtp(currentUid, otpBuffer, action);

  if (result.status == "success") {
    digitalWrite(GREEN_LED_PIN, HIGH);
    beep(1, 200);
    String lateNote = result.isLate ? " (LATE)" : "";
    showMessage3(result.staffName, result.eventType + lateNote, formatTimestamp());
    delay(3000);
    digitalWrite(GREEN_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else if (result.status == "wrong_action") {
    beep(2, 100);
    showMessage("Wrong button.", "Press " + result.expected + " instead");
    delay(2000);
    showMessage("Press TIME IN", "or TIME OUT"); // stay here, OTP still valid
    waitForButtonRelease();
  } else if (result.status == "expired") {
    digitalWrite(RED_LED_PIN, HIGH);
    beep(2, 100);
    showMessage("Code expired.", "Tap your ID again.");
    delay(2000);
    digitalWrite(RED_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else if (result.status == "invalid") {
    digitalWrite(RED_LED_PIN, HIGH);
    beep(3, 100);
    showMessage("Invalid code.", "Tap your ID again.");
    delay(2000);
    digitalWrite(RED_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else {
    digitalWrite(RED_LED_PIN, HIGH);
    beep(2, 150);
    showMessage("Connection", "error. Retry.");
    delay(2000);
    digitalWrite(RED_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  }
}

// Wait for release so one press doesn't get read twice
void waitForButtonRelease() {
  while (digitalRead(BTN_TIME_IN) == HIGH || digitalRead(BTN_TIME_OUT) == HIGH) delay(10);
}

void resetToIdle() {
  state = STATE_IDLE;
  currentUid = "";
  otpBuffer = "";
  showMessage("Ready.", "Tap your ID.");
}

// ---------- API CALLS ----------
ScanResult requestOtp(String uid) {
  ScanResult result = { "error", "", 0 };
  ensureWiFi();
  if (WiFi.status() != WL_CONNECTED) return result;

  WiFiClientSecure client;
  client.setInsecure(); // TODO: pin the Supabase root CA before deployment
  HTTPClient http;
  if (!http.begin(client, SEND_OTP_URL)) return result;
  http.setTimeout(10000);
  http.addHeader("Content-Type", "application/json");
  http.addHeader("x-device-key", DEVICE_API_KEY);

  JsonDocument body;
  body["rfid_uid"] = uid;
  String payload;
  serializeJson(body, payload);

  int code = http.POST(payload);
  if (code == 200) {
    JsonDocument res;
    if (!deserializeJson(res, http.getString())) {
      result.status = res["status"].as<String>();
      if (result.status == "sent") result.staffName = res["staff_name"].as<String>();
      if (result.status == "cooldown") result.waitSeconds = res["wait_seconds"] | 60;
    }
  }
  http.end();
  return result;
}

VerifyResult verifyOtp(String uid, String otp, String action) {
  VerifyResult result = { "error", "", "", false, "" };
  ensureWiFi();
  if (WiFi.status() != WL_CONNECTED) return result;

  WiFiClientSecure client;
  client.setInsecure(); // TODO: pin the Supabase root CA before deployment
  HTTPClient http;
  if (!http.begin(client, VERIFY_OTP_URL)) return result;
  http.setTimeout(10000);
  http.addHeader("Content-Type", "application/json");
  http.addHeader("x-device-key", DEVICE_API_KEY);

  JsonDocument body;
  body["rfid_uid"] = uid;
  body["otp"] = otp;
  body["action"] = action;
  String payload;
  serializeJson(body, payload);

  int code = http.POST(payload);
  if (code == 200) {
    JsonDocument res;
    if (!deserializeJson(res, http.getString())) {
      result.status = res["status"].as<String>();
      if (result.status == "success") {
        result.staffName = res["staff_name"].as<String>();
        result.eventType = res["event_type"].as<String>();
        result.isLate = res["is_late"] | false;
      }
      if (result.status == "wrong_action") {
        result.expected = res["expected"].as<String>();
      }
    }
  }
  http.end();
  return result;
}

// ---------- HELPERS ----------
String bytesToHexString(uint8_t* bytes, uint8_t length) {
  String hex = "";
  for (uint8_t i = 0; i < length; i++) {
    if (bytes[i] < 0x10) hex += "0";
    hex += String(bytes[i], HEX);
  }
  hex.toUpperCase();
  return hex;
}

String formatTimestamp() {
  DateTime now = rtc.now();
  const char* days[] = {"Sun","Mon","Tue","Wed","Thu","Fri","Sat"};
  int hour12 = now.hour() % 12;
  if (hour12 == 0) hour12 = 12;
  const char* ampm = now.hour() < 12 ? "AM" : "PM";
  char buf[32];
  snprintf(buf, sizeof(buf), "%s %02d:%02d:%02d %s", days[now.dayOfTheWeek()],
           hour12, now.minute(), now.second(), ampm);
  return String(buf);
}

void showMessage(String line1, String line2) {
  // Mirror to serial so the device stays debuggable with no screen attached.
  Serial.println("[OLED] " + line1 + " | " + line2);
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 10);
  display.println(line1);
  display.setCursor(0, 30);
  display.println(line2);
  display.display();
}

void showMessage3(String line1, String line2, String line3) {
  Serial.println("[OLED] " + line1 + " | " + line2 + " | " + line3);
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.setCursor(0, 5);
  display.println(line1);
  display.setCursor(0, 25);
  display.println(line2);
  display.setCursor(0, 45);
  display.println(line3);
  display.display();
}

void beep(int times, int durationMs) {
  for (int i = 0; i < times; i++) {
    digitalWrite(BUZZER_PIN, HIGH);
    delay(durationMs);
    digitalWrite(BUZZER_PIN, LOW);
    if (i < times - 1) delay(durationMs);
  }
}
