/*
 * Attendance System - ESP32 device firmware
 *
 * Wiring (matches the approved component list):
 *   RC522  SCK/MISO/MOSI -> GPIO18 / 19 / 23     SDA(SS) -> GPIO5   RST -> GPIO17
 *   OLED + DS3231 (I2C)  -> SDA GPIO21, SCL GPIO22, 3.3V, GND
 *   Keypad rows          -> GPIO13, 12, 14, 27
 *   Keypad cols          -> GPIO15, 33, 25, 32
 *   Green LED GPIO2, Red LED GPIO16, Buzzer GPIO26
 *   Time-In GPIO34, Time-Out GPIO35 (input-only: external 10k pull-down required)
 *   DFPlayer Mini: VCC->5V, GND->GND (common with ESP32 GND),
 *                  DFPlayer RX <- 1k resistor <- ESP32 GPIO4  (ESP32 TX)
 *                  DFPlayer TX -> ESP32 GPIO36 / VP        (ESP32 RX)
 *                  SPK_1 / SPK_2 -> speaker (or DAC_R/DAC_L -> amplifier)
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
#include <DFRobotDFPlayerMini.h>
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

// --- DFPlayer Mini (UART2, two-way) ---
// GPIO36 is input-only with no internal pull-up, so the core logs a
// harmless "gpio_pullup_en ... GPIO number error" at boot. The DFPlayer
// drives its TX line itself, so no pull-up is needed and RX works fine.
#define DFPLAYER_TX_PIN 4    // ESP32 transmits here -> DFPlayer RX (via 1k)
#define DFPLAYER_RX_PIN 36   // DFPlayer TX -> ESP32 receives here
#define DFPLAYER_VOLUME 30   // 0..30
DFRobotDFPlayerMini dfplayer;
bool audioReady = false;
bool useRootOrder = false;         // set when /mp3/000N.mp3 is not found
uint8_t lastClip = 0;              // replayed once after switching to root order
unsigned long voiceStartedAt = 0;  // millis() when the current clip started
uint16_t voiceLenMs = 0;           // length of the current clip, 0 if none

// Voice clips on the SD card in a folder named "mp3": /mp3/0001.mp3 ... /mp3/0007.mp3
enum VoiceClip {
  VOICE_NONE          = 0,
  VOICE_CARD_DETECTED = 1,  // "Card detected, please enter OTP"
  VOICE_OTP_SENT      = 2,  // "OTP sent to your email"
  VOICE_TIME_IN_OK    = 3,  // "Time-in recorded successfully"
  VOICE_TIME_OUT_OK   = 4,  // "Time-out recorded successfully"
  VOICE_WRONG_OTP     = 5,  // "Incorrect OTP, please try again"
  VOICE_OTP_EXPIRED   = 6,  // "OTP expired, please tap again"
  VOICE_UNREGISTERED  = 7   // "Card not recognized"
};

// Length of each clip in milliseconds, indexed by VoiceClip.
// >>> MEASURE YOUR OWN FILES AND EDIT THESE <<<  (right-click the mp3 ->
// Properties -> Details -> Length, then round UP to the next 100 ms).
// The screen and the LED are held at least this long so a clip is never
// cut off by the next state change.
const uint16_t VOICE_MS[] = {
  0,      // VOICE_NONE
  2500,   // 0001 card detected
  2000,   // 0002 OTP sent
  2000,   // 0003 time-in success
  2000,   // 0004 time-out success
  2500,   // 0005 wrong OTP
  2500,   // 0006 OTP expired
  1500    // 0007 unregistered card
};
// Silence left after a clip finishes before moving on.
const uint16_t VOICE_TAIL_MS = 300;

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
void playVoice(uint8_t clip);
void feedback(int beeps, int beepMs, uint8_t clip);
void waitForFeedback(unsigned long minMs);
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

  initAudio();
  connectWiFi();
  syncRtcFromNtp();
  showMessage("Ready.", "Tap your ID.");
}

void initAudio() {
  Serial2.begin(9600, SERIAL_8N1, DFPLAYER_RX_PIN, DFPLAYER_TX_PIN);

  // The module needs ~1.5s after power-on before it accepts commands.
  delay(1500);

  // isACK = true: every command waits for the module to confirm it.
  // doReset = true: reset and wait for the "card online" report, so a
  // true result means the module is alive AND sees the SD card.
  audioReady = dfplayer.begin(Serial2, /*isACK=*/true, /*doReset=*/true);

  if (audioReady) {
    dfplayer.setTimeOut(500);
    dfplayer.outputDevice(DFPLAYER_DEVICE_SD);
    dfplayer.volume(DFPLAYER_VOLUME);
    delay(100);
    // -1 here means the module did not answer that query (some clones don't).
    Serial.printf("DFPlayer online. Volume=%d, files on SD=%d\n",
                  dfplayer.readVolume(), dfplayer.readFileCounts());
    // Boot check: if this is heard but the prompts later are not, WiFi is
    // pulling the supply down while it transmits.
    playVoice(VOICE_CARD_DETECTED);
  } else {
    Serial.println(F("DFPlayer NOT responding. Check: DFPlayer TX -> GPIO36, "
                     "RX <- 1k <- GPIO4, common GND, SD card inserted."));
    printAudioStatus(); // shows the module's own error, if it sent one
  }
}

// Starts a clip and returns immediately - playback runs on the DFPlayer,
// not on the ESP32. Use waitForFeedback() to hold the screen until it ends.
void playVoice(uint8_t clip) {
  voiceStartedAt = millis();
  voiceLenMs = 0;
  if (clip == VOICE_NONE) return;
  if (!audioReady) {
    Serial.printf("[AUDIO] skipped clip %u - DFPlayer not ready\n", clip);
    return;
  }
  lastClip = clip;
  if (useRootOrder) {
    // Fallback: the Nth file on the card, in the order files were copied.
    Serial.printf("[AUDIO] play file #%u (root order)\n", clip);
    dfplayer.play(clip);
  } else {
    // Plays /mp3/000<clip>.mp3 by file NAME.
    Serial.printf("[AUDIO] play /mp3/%04u.mp3\n", clip);
    dfplayer.playMp3Folder(clip);
  }
  if (clip < sizeof(VOICE_MS) / sizeof(VOICE_MS[0])) voiceLenMs = VOICE_MS[clip];
}

// Buzzer FIRST, then the voice prompt. The beep is the attention cue; the
// clip explains what happened. beep() blocks, so the order here is the order
// the user hears.
void feedback(int beeps, int beepMs, uint8_t clip) {
  beep(beeps, beepMs);
  playVoice(clip);
}

// Holds for whichever is longer: the rest of the clip, or minMs of screen
// time. Falls back to minMs when the DFPlayer is missing, so the system
// still behaves sanely with no audio attached.
void waitForFeedback(unsigned long minMs) {
  unsigned long need = minMs;
  if (audioReady && voiceLenMs > 0) {
    unsigned long clipTime = (unsigned long)voiceLenMs + VOICE_TAIL_MS;
    if (clipTime > need) need = clipTime;
  }
  unsigned long elapsed = millis() - voiceStartedAt;
  if (elapsed < need) delay(need - elapsed);
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
// Prints anything the DFPlayer reported: finished clips, SD card errors,
// missing files, resets (a reset mid-use usually means a power dip).
void printAudioStatus() {
  if (!dfplayer.available()) return;
  uint8_t type = dfplayer.readType();
  int value = dfplayer.read();
  switch (type) {
    case TimeOut:            Serial.println(F("[AUDIO] no reply (timeout)")); break;
    case WrongStack:         Serial.println(F("[AUDIO] garbled reply")); break;
    case DFPlayerCardInserted: Serial.println(F("[AUDIO] SD card inserted")); break;
    case DFPlayerCardRemoved:  Serial.println(F("[AUDIO] SD card REMOVED")); break;
    case DFPlayerCardOnline:   Serial.println(F("[AUDIO] SD card online")); break;
    case DFPlayerPlayFinished: Serial.printf("[AUDIO] finished clip %d\n", value); break;
    case DFPlayerError:
      Serial.print(F("[AUDIO] ERROR: "));
      switch (value) {
        case Busy:             Serial.println(F("card not found / busy")); break;
        case Sleeping:         Serial.println(F("sleeping")); break;
        case SerialWrongStack: Serial.println(F("bad serial frame")); break;
        case CheckSumNotMatch: Serial.println(F("checksum mismatch")); break;
        case FileIndexOut:     Serial.println(F("file number out of range")); break;
        case FileMismatch:
          Serial.println(F("file not found"));
          // No /mp3/000N.mp3 on this card: switch to copy-order playback
          // for the rest of this session and retry the clip that failed.
          if (!useRootOrder) {
            useRootOrder = true;
            Serial.println(F("[AUDIO] /mp3 folder not usable - falling back to root order"));
            if (lastClip != VOICE_NONE) playVoice(lastClip);
          }
          break;
        case Advertise:        Serial.println(F("in advertise")); break;
        default:               Serial.printf("code %d\n", value); break;
      }
      break;
    default: Serial.printf("[AUDIO] message type %u value %d\n", type, value); break;
  }
}

void loop() {
  if (audioReady) printAudioStatus();
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

  // Short confirm beep the instant the card reads, then the prompt clip.
  feedback(1, 80, VOICE_CARD_DETECTED);
  showMessage("Reading card...", "");
  ScanResult result = requestOtp(uid);

  if (result.status == "sent") {
    // The network round-trip already ate part of clip 0001; let it finish
    // before 0002 starts, or the DFPlayer cuts it off mid-sentence.
    waitForFeedback(0);
    playVoice(VOICE_OTP_SENT);
    currentUid = uid;
    otpBuffer = "";
    otpStartTime = millis();
    state = STATE_AWAITING_OTP;
    showMessage("Hi " + result.staffName + "!", "Enter emailed code:");
  } else if (result.status == "cooldown") {
    feedback(1, 150, VOICE_NONE);
    showMessage("Please wait", String(result.waitSeconds) + "s and retap");
    waitForFeedback(2000);
    showMessage("Ready.", "Tap your ID.");
  } else if (result.status == "unregistered") {
    feedback(3, 100, VOICE_UNREGISTERED);
    showMessage("Card not", "registered.");
    waitForFeedback(2000);
    showMessage("Ready.", "Tap your ID.");
  } else {
    feedback(2, 150, VOICE_NONE);
    showMessage("Connection", "error. Retry.");
    waitForFeedback(2000);
    showMessage("Ready.", "Tap your ID.");
  }
}

void handleAwaitingOtp() {
  if (millis() - otpStartTime > OTP_TIMEOUT_MS) {
    feedback(2, 150, VOICE_OTP_EXPIRED);
    showMessage("Code expired.", "Please tap again.");
    waitForFeedback(2000);
    resetToIdle();
    return;
  }

  char key = keypad.getKey();
  if (!key) return;
  beep(1, 40); // short click so every key press is confirmed

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
    feedback(2, 150, VOICE_OTP_EXPIRED);
    showMessage("Code expired.", "Please tap again.");
    waitForFeedback(2000);
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
    feedback(1, 200, result.eventType == "TIME-OUT" ? VOICE_TIME_OUT_OK : VOICE_TIME_IN_OK);
    String lateNote = result.isLate ? " (LATE)" : "";
    showMessage3(result.staffName, result.eventType + lateNote, formatTimestamp());
    waitForFeedback(3000);
    digitalWrite(GREEN_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else if (result.status == "wrong_action") {
    feedback(2, 100, VOICE_NONE);
    showMessage("Wrong button.", "Press " + result.expected + " instead");
    waitForFeedback(2000);
    showMessage("Press TIME IN", "or TIME OUT"); // stay here, OTP still valid
    waitForButtonRelease();
  } else if (result.status == "expired") {
    digitalWrite(RED_LED_PIN, HIGH);
    feedback(2, 100, VOICE_OTP_EXPIRED);
    showMessage("Code expired.", "Tap your ID again.");
    waitForFeedback(2000);
    digitalWrite(RED_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else if (result.status == "invalid") {
    digitalWrite(RED_LED_PIN, HIGH);
    feedback(3, 100, VOICE_WRONG_OTP);
    showMessage("Invalid code.", "Tap your ID again.");
    waitForFeedback(2000);
    digitalWrite(RED_LED_PIN, LOW);
    waitForButtonRelease();
    resetToIdle();
  } else {
    digitalWrite(RED_LED_PIN, HIGH);
    feedback(2, 150, VOICE_NONE);
    showMessage("Connection", "error. Retry.");
    waitForFeedback(2000);
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
