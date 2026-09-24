// DFPlayer Mini test - plays /mp3/0001.mp3 ... /mp3/0007.mp3 one after another.
// Same wiring as attendance_device:
//   DFPlayer VCC -> 5V (VIN), GND -> GND (shared with ESP32)
//   DFPlayer RX  <- 1k resistor <- ESP32 GPIO 4
//   DFPlayer SPK_1 / SPK_2 -> speaker (NOT GND)
// SD card: FAT32, folder "mp3" in the root containing 0001.mp3, 0002.mp3, ...

#include <DFRobotDFPlayerMini.h>

#define DFPLAYER_TX_PIN 4
#define DFPLAYER_RX_PIN -1

DFRobotDFPlayerMini dfplayer;

void setup() {
  Serial.begin(115200);
  Serial2.begin(9600, SERIAL_8N1, DFPLAYER_RX_PIN, DFPLAYER_TX_PIN);
  delay(2000); // let the module boot and scan the SD card

  dfplayer.begin(Serial2, /*isACK=*/false, /*doReset=*/false);
  delay(200);
  dfplayer.outputDevice(DFPLAYER_DEVICE_SD);
  delay(200);
  dfplayer.volume(30); // max, so a quiet speaker isn't mistaken for silence
  delay(200);
  Serial.println("DFPlayer test started.");
}

void loop() {
  for (uint8_t clip = 1; clip <= 7; clip++) {
    Serial.printf("Playing /mp3/%04d.mp3\n", clip);
    dfplayer.playMp3Folder(clip);
    delay(4000);
  }
}
