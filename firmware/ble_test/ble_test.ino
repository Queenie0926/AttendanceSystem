/*
 * BLE-only test - diagnostic, not part of the attendance device.
 *
 * Same purpose as wifi_test: find out whether the board survives turning on
 * the radio. BLE peaks around 130mA against Wi-Fi's ~400mA, so a marginal
 * supply may survive this where it failed there.
 *
 * PHYSICALLY DISCONNECT EVERYTHING before running this - same as wifi_test.
 *
 * Reads:
 *   Advertises and keeps printing uptime  -> the board tolerates BLE's
 *       smaller spike. It does NOT mean the board is healthy: a regulator
 *       that cannot supply 400mA is still faulty.
 *   Brownout anyway  -> the supply cannot even manage 130mA. Replace the
 *       board; there is nothing left to try.
 *
 * Note: BLE is a local link with no internet access. This sketch proves the
 * radio powers up - it does not reach Supabase. See the notes in chat about
 * what a BLE build of the attendance device would actually require.
 */

#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>

#define SERVICE_UUID        "6e400001-b5a3-f393-e0a9-e50e24dcca9e"
#define CHARACTERISTIC_UUID "6e400003-b5a3-f393-e0a9-e50e24dcca9e"

BLECharacteristic* statusChar = nullptr;
bool clientConnected = false;

class ServerCallbacks : public BLEServerCallbacks {
  void onConnect(BLEServer* server) override {
    clientConnected = true;
    Serial.println(F("Client connected."));
  }
  void onDisconnect(BLEServer* server) override {
    clientConnected = false;
    Serial.println(F("Client disconnected - advertising again."));
    BLEDevice::startAdvertising();
  }
};

void setup() {
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println(F("=== BLE only test ==="));
  Serial.print(F("Chip: "));
  Serial.println(ESP.getChipModel());

  Serial.println(F("Initialising BLE controller..."));
  BLEDevice::init("AttendanceDevice");
  // Surviving to here means the controller powered up without a brownout.
  Serial.println(F("BLE controller up - no brownout during init."));

  BLEServer* server = BLEDevice::createServer();
  server->setCallbacks(new ServerCallbacks());

  BLEService* service = server->createService(SERVICE_UUID);
  statusChar = service->createCharacteristic(
    CHARACTERISTIC_UUID,
    BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY
  );
  statusChar->setValue("ready");
  service->start();

  BLEAdvertising* advertising = BLEDevice::getAdvertising();
  advertising->addServiceUUID(SERVICE_UUID);
  advertising->setScanResponse(true);
  BLEDevice::startAdvertising();

  // Advertising transmits periodically - this is the sustained load.
  Serial.println(F("Advertising as 'AttendanceDevice'."));
  Serial.println(F("Scan for it with nRF Connect or your phone's BT menu."));
}

void loop() {
  static uint32_t seconds = 0;
  seconds = millis() / 1000;

  Serial.print(F("up "));
  Serial.print(seconds);
  Serial.print(F("s, "));
  Serial.println(clientConnected ? F("client connected") : F("advertising"));

  if (statusChar != nullptr) {
    String value = "up " + String(seconds) + "s";
    statusChar->setValue(value.c_str());
    if (clientConnected) statusChar->notify(); // notify = extra TX activity
  }

  delay(2000);
}
